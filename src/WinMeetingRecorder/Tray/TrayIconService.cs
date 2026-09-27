using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinMeetingRecorder.Audio;
using WinMeetingRecorder.Workflow;

namespace WinMeetingRecorder.Tray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly TrayIconService _trayService;

    public TrayApplicationContext(
        RecordingWorkflowService workflow, 
        AudioEngine audioEngine, 
        Func<Task> onShutdownRequested)
    {
        _trayService = new TrayIconService(workflow, audioEngine, async () =>
        {
            await onShutdownRequested();
            ExitThread();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayService.Dispose();
        }
        base.Dispose(disposing);
    }
}

public class TrayIconService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly RecordingWorkflowService _workflow;
    private readonly AudioEngine _audioEngine;
    private readonly Action _onExitRequested;

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _headerItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ToolStripMenuItem _screenshotItem;
    private readonly ToolStripMenuItem _openWebItem;
    private readonly ToolStripMenuItem _exitItem;

    private readonly System.Windows.Forms.Timer _timer;
    private IntPtr _lastHIcon = IntPtr.Zero;
    private int _tickCount = 0;
    private bool _isDisposed = false;

    public TrayIconService(
        RecordingWorkflowService workflow, 
        AudioEngine audioEngine,
        Action onExitRequested)
    {
        _workflow = workflow;
        _audioEngine = audioEngine;
        _onExitRequested = onExitRequested;

        // コンテキストメニューの構築
        _contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = false
        };

        _headerItem = new ToolStripMenuItem("🎙️ WinMeetingRecorder: 待機中")
        {
            Enabled = false,
            Font = new Font(Control.DefaultFont, FontStyle.Bold)
        };

        _startItem = new ToolStripMenuItem("🔴 会議の記録を開始", null, async (s, e) =>
        {
            if (s is ToolStripMenuItem item) item.Enabled = false;
            var res = await _workflow.StartRecordingAsync();
            if (!res.Success)
            {
                ShowBalloon("⚠️ 開始エラー", res.Message, ToolTipIcon.Warning);
                UpdateMenuState();
            }
        });

        _stopItem = new ToolStripMenuItem("⏹️ 記録を停止 (保存/AI処理)", null, async (s, e) =>
        {
            if (s is ToolStripMenuItem item) item.Enabled = false;
            ShowBalloon("⏹️ 記録停止", "会議記録を停止しました。MP3圧縮・AI議事録・GitLab同期をバックグラウンドで実行中...", ToolTipIcon.Info);
            var res = await _workflow.StopRecordingAsync();
            if (!res.Success)
            {
                ShowBalloon("⚠️ 停止エラー", res.Message, ToolTipIcon.Warning);
            }
            UpdateMenuState();
        })
        {
            Enabled = false
        };

        _screenshotItem = new ToolStripMenuItem("📸 スクリーンショット撮影", null, (s, e) =>
        {
            var evt = _workflow.CaptureManual();
            if (evt != null)
            {
                ShowBalloon("📸 撮影完了", $"手動スクショを撮影しました ({evt.FileName})", ToolTipIcon.Info);
            }
            else
            {
                ShowBalloon("⚠️ 撮影スキップ", "画面キャプチャが有効な録音中のみ撮影できます。", ToolTipIcon.Warning);
            }
        })
        {
            Enabled = false
        };

        _openWebItem = new ToolStripMenuItem("🌐 操作盤を開く (Web UI: localhost:5000)", null, (s, e) =>
        {
            OpenWebUi();
        });

        _exitItem = new ToolStripMenuItem("❌ アプリケーションを終了", null, (s, e) =>
        {
            _onExitRequested?.Invoke();
        });

        _contextMenu.Items.Add(_headerItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_startItem);
        _contextMenu.Items.Add(_stopItem);
        _contextMenu.Items.Add(_screenshotItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_openWebItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_exitItem);

        // NotifyIcon の初期化 (まずアイコンを描画してから表示フラグを立てる)
        _notifyIcon = new NotifyIcon
        {
            Text = "WinMeetingRecorder (待機中)",
            ContextMenuStrip = _contextMenu
        };

        // 初期アイコン (待機中アイコン) を描画して適用
        SetIdleIcon();
        _notifyIcon.Visible = true;

        // クリックイベント: 右クリック・左クリック問わず即座にメニューを表示
        _notifyIcon.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
            {
                UpdateMenuState();
                SetForegroundWindow(_contextMenu.Handle);
                _contextMenu.Show(Cursor.Position);
            }
        };

        // ダブルクリックで Web UI を開く
        _notifyIcon.DoubleClick += (s, e) => OpenWebUi();
        _notifyIcon.BalloonTipClicked += (s, e) => OpenWebUi();

        // 録音中アニメーションタイマー (150ms 周期)
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 150
        };
        _timer.Tick += (s, e) =>
        {
            if (_workflow.IsRecording)
            {
                UpdateRecordingWaveformIcon();
            }
            else
            {
                _timer.Stop();
                SetIdleIcon();
            }
        };

        // ワークフローイベントの購読
        _workflow.RecordingStarted += sessionId =>
        {
            UpdateMenuState();
            _timer.Start(); // 録音中のみ波形アニメーションタイマーを起動
            ShowBalloon("🎙️ 会議記録開始", "相手の声・自分の声の合成録音と画面監視を開始しました。\nトレイアイコンの緑色波形で音声入力状況を確認できます。", ToolTipIcon.Info);
        };

        _workflow.RecordingStopped += sessionId =>
        {
            _timer.Stop();
            SetIdleIcon();
            UpdateMenuState();
        };

        _workflow.PipelineCompleted += msg =>
        {
            ShowBalloon("✅ 保存完了", msg, ToolTipIcon.Info);
        };

        _workflow.PipelineFailed += (step, err) =>
        {
            ShowBalloon("⚠️ 自動処理エラー", $"ステップ '{step}': {err} (録音ファイルは保護されています)", ToolTipIcon.Warning);
        };

        // 起動通知 (タスクトレイ内での存在をユーザーにお知らせ)
        ShowBalloon("🎙️ WinMeetingRecorder 起動完了", "タスクトレイに常駐しました。\n右クリックまたはクリックで【記録開始/停止】が可能です。", ToolTipIcon.Info);
    }

    /// <summary>
    /// メニューの活性/非活性状態を更新
    /// </summary>
    private void UpdateMenuState()
    {
        if (_isDisposed) return;

        bool isRecording = _workflow.IsRecording;
        _startItem.Enabled = !isRecording;
        _stopItem.Enabled = isRecording;
        _screenshotItem.Enabled = isRecording;

        if (isRecording)
        {
            var elapsed = _workflow.ElapsedTime;
            string timeStr = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            int levelPct = (int)(_audioEngine.CurrentAudioLevel * 100);
            _headerItem.Text = $"🔴 録音中 ({timeStr}) [音声: {levelPct}%]";
        }
        else
        {
            _headerItem.Text = "🎙️ WinMeetingRecorder: 待機中";
        }
    }

    /// <summary>
    /// 待機中アイコンの設定 (静止アイコン・1回のみ描画)
    /// </summary>
    private void SetIdleIcon()
    {
        if (_isDisposed) return;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // 背景: 角丸スレートダーク
            using (var bgBrush = new SolidBrush(Color.FromArgb(235, 30, 41, 59)))
            {
                FillRoundedRectangle(g, bgBrush, new Rectangle(0, 0, 31, 31), 6);
            }

            // マイクアイコン (スレートブルー / 白)
            using (var micBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
            {
                // マイク頭部 (角丸)
                FillRoundedRectangle(g, micBrush, new Rectangle(11, 6, 10, 13), 4);

                // マイクスタンド弧線
                using var pen = new Pen(micBrush, 2);
                g.DrawArc(pen, 8, 9, 16, 13, 0, 180);
                // 支柱 & 台座
                g.DrawLine(pen, 16, 22, 16, 26);
                g.DrawLine(pen, 11, 26, 21, 26);
            }
        }

        ApplyBitmapAsIcon(bmp, "WinMeetingRecorder (待機中 - クリックでメニュー)");
    }

    /// <summary>
    /// 録音中リアルタイム波形アイコンの更新 (150ms周期で跳ねる動的イコライザー)
    /// </summary>
    private void UpdateRecordingWaveformIcon()
    {
        if (_isDisposed) return;

        _tickCount++;
        float audioLevel = _audioEngine.CurrentAudioLevel; // 0.0f ~ 1.0f
        bool hasVoice = audioLevel > 0.03f;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // 背景: 深いダークスレート
            using (var bgBrush = new SolidBrush(Color.FromArgb(245, 15, 23, 42)))
            {
                FillRoundedRectangle(g, bgBrush, new Rectangle(0, 0, 31, 31), 6);
            }

            // 左上: 鮮やかな赤丸 (● REC)
            using (var recBrush = new SolidBrush(Color.FromArgb(239, 68, 68)))
            {
                g.FillEllipse(recBrush, 3, 4, 7, 7);
            }

            // 4本のイコライザー波形バー
            Color barColor = hasVoice 
                ? Color.FromArgb(34, 197, 94)   // 鮮やかなエメラルドグリーン (音声検知中!)
                : Color.FromArgb(100, 116, 139); // スレートグレー (無音)

            int[] barX = [13, 17, 21, 25];
            float[] factors = [0.9f, 1.4f, 1.2f, 0.8f];
            int maxHeight = 20;
            int baseY = 26;

            using var barBrush = new SolidBrush(barColor);
            for (int i = 0; i < 4; i++)
            {
                int h;
                if (!hasVoice)
                {
                    h = 3; // 無音時の最低高
                }
                else
                {
                    // 音量レベルに周波数係数と揺らぎを加算してイコライザー感を演出
                    float factor = factors[i];
                    float jitter = ((i * 2 + _tickCount) % 5) * 0.06f;
                    float val = Math.Clamp(audioLevel * factor + jitter, 0.15f, 1.0f);
                    h = Math.Max(3, (int)(val * maxHeight));
                }

                int x = barX[i];
                int y = baseY - h;
                g.FillRectangle(barBrush, x, y, 3, h);
            }
        }

        var elapsed = _workflow.ElapsedTime;
        string timeStr = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        int levelPct = (int)(audioLevel * 100);
        string tooltip = $"WinMeetingRecorder (録音中: {timeStr} | 音声: {levelPct}%)";
        if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);

        ApplyBitmapAsIcon(bmp, tooltip);
    }

    /// <summary>
    /// Bitmap から HICON を作成し、NotifyIcon に安全に適用 (メモリリーク完全防止)
    /// </summary>
    private void ApplyBitmapAsIcon(Bitmap bmp, string tooltipText)
    {
        IntPtr hIcon = bmp.GetHicon();
        var oldHIcon = _lastHIcon;
        _lastHIcon = hIcon;

        _notifyIcon.Icon = Icon.FromHandle(hIcon);
        _notifyIcon.Text = tooltipText;

        // 古い HICON ハンドルを毎回確実に破棄 (GDI/USER ハンドルリーク防止)
        if (oldHIcon != IntPtr.Zero)
        {
            DestroyIcon(oldHIcon);
        }
    }

    /// <summary>
    /// 角丸四角形を描画するヘルパー
    /// </summary>
    private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle bounds, int cornerRadius)
    {
        using var path = new GraphicsPath();
        int diameter = cornerRadius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        // 左上
        path.AddArc(arc, 180, 90);
        // 右上
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        // 右下
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        // 左下
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        g.FillPath(brush, path);
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, text, icon);
        }
        catch { /* ignore */ }
    }

    private static void OpenWebUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo("http://localhost:5000") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Tray] ブラウザ起動エラー: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _timer.Stop();
        _timer.Dispose();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();

        if (_lastHIcon != IntPtr.Zero)
        {
            DestroyIcon(_lastHIcon);
            _lastHIcon = IntPtr.Zero;
        }
    }
}

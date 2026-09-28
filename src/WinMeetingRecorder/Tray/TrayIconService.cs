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
    private readonly SynchronizationContext? _syncContext;

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _headerItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ToolStripMenuItem _screenshotItem;
    private readonly ToolStripMenuItem _openWebItem;
    private readonly ToolStripMenuItem _exitItem;

    private CancellationTokenSource? _animCts;
    private CancellationTokenSource? _monitorCts;
    private int _tickCount = 0;
    private bool _isDisposed = false;
    private bool _currentlyShowingRecordingState = false;

    public TrayIconService(
        RecordingWorkflowService workflow, 
        AudioEngine audioEngine,
        Action onExitRequested)
    {
        _workflow = workflow;
        _audioEngine = audioEngine;
        _onExitRequested = onExitRequested;
        _syncContext = SynchronizationContext.Current;

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
            ShowBalloon("⏹️ 記録停止", "会議記録を停止しました。MP3圧縮・AI議事録・GitLab同期を実行中...", ToolTipIcon.Info);
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
                ShowBalloon("⚠️ 撮影スキップ", "録音中のみ撮影できます。", ToolTipIcon.Warning);
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

        // NotifyIcon の初期化
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

        // ワークフローイベントの購読 (スレッドセーフに UI スレッドにマーシャリング)
        _workflow.RecordingStarted += sessionId =>
        {
            PostToUiThread(() =>
            {
                StartAnimation();
                UpdateMenuState();
                ShowBalloon("🎙️ 会議記録開始", "相手の声・自分の声の合成録音を開始しました。\nトレイアイコンの緑色波形で音声入力状況を確認できます。", ToolTipIcon.Info);
            });
        };

        _workflow.RecordingStopped += sessionId =>
        {
            PostToUiThread(() =>
            {
                StopAnimation();
                UpdateMenuState();
            });
        };

        _workflow.PipelineCompleted += msg =>
        {
            PostToUiThread(() => ShowBalloon("✅ 保存完了", msg, ToolTipIcon.Info));
        };

        _workflow.PipelineFailed += (step, err) =>
        {
            PostToUiThread(() => ShowBalloon("⚠️ 自動処理エラー", $"ステップ '{step}': {err}", ToolTipIcon.Warning));
        };

        // 自己修復バックグラウンド監視 (イベント取りこぼし対策・500ms周期)
        StartMonitorLoop();

        // 起動通知 (タスクトレイ内での存在をユーザーにお知らせ)
        ShowBalloon("🎙️ WinMeetingRecorder 常駐中", "タスクトレイに常駐しました。クリックで記録開始・停止が可能です。", ToolTipIcon.Info);
    }

    private void PostToUiThread(Action action)
    {
        if (_isDisposed) return;
        if (_syncContext != null)
        {
            _syncContext.Post(_ =>
            {
                if (!_isDisposed) action();
            }, null);
        }
        else
        {
            action();
        }
    }

    /// <summary>
    /// 自己修復ステータス監視ループ (Web UIや外部APIで状態が変わった場合でも確実にトレイを同期)
    /// </summary>
    private void StartMonitorLoop()
    {
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    await Task.Delay(500, token);
                    bool isRec = _workflow.IsRecording;
                    if (isRec && !_currentlyShowingRecordingState)
                    {
                        PostToUiThread(() => StartAnimation());
                    }
                    else if (!isRec && _currentlyShowingRecordingState)
                    {
                        PostToUiThread(() => StopAnimation());
                    }
                }
                catch { /* ignore */ }
            }
        }, token);
    }

    private void StartAnimation()
    {
        if (_isDisposed) return;
        _currentlyShowingRecordingState = true;
        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        var token = _animCts.Token;

        UpdateMenuState();

        // 120ms 周期で波形アニメーション描画
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !_isDisposed && _workflow.IsRecording)
            {
                PostToUiThread(() => UpdateRecordingWaveformIcon());
                try
                {
                    await Task.Delay(120, token);
                }
                catch { break; }
            }
        }, token);
    }

    private void StopAnimation()
    {
        if (_isDisposed) return;
        _currentlyShowingRecordingState = false;
        _animCts?.Cancel();
        _animCts = null;

        SetIdleIcon();
        UpdateMenuState();
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
    /// 待機中アイコンの設定 (濃紺背景 + 水色マイクマーク)
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

            // 背景: 丸角の濃紺・ダークスレート (#1E293B)
            using (var bgBrush = new SolidBrush(Color.FromArgb(240, 30, 41, 59)))
            {
                FillRoundedRectangle(g, bgBrush, new Rectangle(0, 0, 31, 31), 6);
            }

            // 外周枠線 (視認性を高めるスレート枠)
            using (var borderPen = new Pen(Color.FromArgb(71, 85, 105), 1.5f))
            {
                DrawRoundedRectangle(g, borderPen, new Rectangle(1, 1, 29, 29), 5);
            }

            // マイクアイコン (水色 / シアン #38BDF8)
            using (var micBrush = new SolidBrush(Color.FromArgb(56, 189, 248)))
            {
                // マイク頭部 (角丸)
                FillRoundedRectangle(g, micBrush, new Rectangle(11, 5, 10, 14), 4);

                // マイクスタンド弧線
                using var pen = new Pen(micBrush, 2.2f);
                g.DrawArc(pen, 7, 8, 18, 14, 0, 180);
                // 支柱 & 台座
                g.DrawLine(pen, 16, 22, 16, 27);
                g.DrawLine(pen, 10, 27, 22, 27);
            }
        }

        ApplyBitmapAsIcon(bmp, "WinMeetingRecorder (待機中 - クリックでメニュー)");
    }

    /// <summary>
    /// 録音中リアルタイム波形アイコンの更新 (赤背景枠 + 赤丸REC + 跳ねる鮮やか緑バー)
    /// </summary>
    private void UpdateRecordingWaveformIcon()
    {
        if (_isDisposed) return;

        _tickCount++;
        float audioLevel = _audioEngine.CurrentAudioLevel; // 0.0f ~ 1.0f
        bool hasVoice = audioLevel > 0.02f;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // 背景: 深い黒・ダークレッド (#0F1117)
            using (var bgBrush = new SolidBrush(Color.FromArgb(250, 15, 17, 23)))
            {
                FillRoundedRectangle(g, bgBrush, new Rectangle(0, 0, 31, 31), 6);
            }

            // 録音中外周赤枠 (録音中であることが一目でわかる!)
            using (var recBorderPen = new Pen(Color.FromArgb(239, 68, 68), 1.8f))
            {
                DrawRoundedRectangle(g, recBorderPen, new Rectangle(1, 1, 29, 29), 5);
            }

            // 左上: 鮮烈に光る赤丸 (● REC)
            using (var recDotBrush = new SolidBrush(Color.FromArgb(239, 68, 68)))
            {
                g.FillEllipse(recDotBrush, 4, 5, 7, 7);
            }

            // 4本のイコライザー波形バー
            // 音声検知中は鮮烈なネオングリーン (#22C55E)、無音時は薄いスレートグリーン
            Color barColor = hasVoice 
                ? Color.FromArgb(34, 197, 94)   // 鮮烈エメラルドグリーン (音声拾えてる!)
                : Color.FromArgb(51, 65, 85);    // 無音時ベース

            int[] barX = [13, 17, 21, 25];
            float[] factors = [0.9f, 1.5f, 1.3f, 0.8f];
            int maxHeight = 21;
            int baseY = 26;

            using var barBrush = new SolidBrush(barColor);
            for (int i = 0; i < 4; i++)
            {
                int h;
                if (!hasVoice)
                {
                    h = 3; // 無音時でもバーの存在がわかる
                }
                else
                {
                    float factor = factors[i];
                    float jitter = ((i * 2 + _tickCount) % 4) * 0.08f;
                    float val = Math.Clamp(audioLevel * factor + jitter, 0.15f, 1.0f);
                    h = Math.Max(4, (int)(val * maxHeight));
                }

                int x = barX[i];
                int y = baseY - h;
                g.FillRectangle(barBrush, x, y, 3, h);
            }
        }

        var elapsed = _workflow.ElapsedTime;
        string timeStr = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        int levelPct = (int)(audioLevel * 100);
        string tooltip = $"WinMeetingRecorder [録音中: {timeStr} | 音声: {levelPct}%]";
        if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);

        ApplyBitmapAsIcon(bmp, tooltip);
    }

    /// <summary>
    /// Bitmap から安全な Icon クローンを作成して適用し、ネイティブ HICON を即時解放
    /// </summary>
    private void ApplyBitmapAsIcon(Bitmap bmp, string tooltipText)
    {
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            var oldIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = (Icon)tempIcon.Clone();
            oldIcon?.Dispose();
        }
        catch { /* ignore */ }
        finally
        {
            // ネイティブ HICON ハンドルを毎回即座に解放 (GDIリーク完全防止)
            if (hIcon != IntPtr.Zero)
            {
                DestroyIcon(hIcon);
            }
        }

        _notifyIcon.Text = tooltipText;
    }

    private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle bounds, int cornerRadius)
    {
        using var path = CreateRoundedRectanglePath(bounds, cornerRadius);
        g.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle bounds, int cornerRadius)
    {
        using var path = CreateRoundedRectanglePath(bounds, cornerRadius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int cornerRadius)
    {
        var path = new GraphicsPath();
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
        return path;
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

        _animCts?.Cancel();
        _monitorCts?.Cancel();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}

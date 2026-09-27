using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinMeetingRecorder.Audio;
using WinMeetingRecorder.Workflow;

namespace WinMeetingRecorder.Tray;

public class TrayIconService : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

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
        _contextMenu = new ContextMenuStrip();

        _headerItem = new ToolStripMenuItem("🎙️ WinMeetingRecorder (待機中)")
        {
            Enabled = false,
            Font = new Font(Control.DefaultFont, FontStyle.Bold)
        };

        _startItem = new ToolStripMenuItem("🔴 記録開始", null, async (s, e) =>
        {
            var res = await _workflow.StartRecordingAsync();
            if (!res.Success)
            {
                ShowBalloon("⚠️ 開始エラー", res.Message, ToolTipIcon.Warning);
            }
        });

        _stopItem = new ToolStripMenuItem("⏹️ 記録停止", null, async (s, e) =>
        {
            _stopItem.Enabled = false;
            ShowBalloon("⏹️ 記録停止", "会議記録を停止しました。MP3変換・AI処理を実行中...", ToolTipIcon.Info);
            var res = await _workflow.StopRecordingAsync();
            if (!res.Success)
            {
                ShowBalloon("⚠️ 停止エラー", res.Message, ToolTipIcon.Warning);
            }
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
        })
        {
            Enabled = false
        };

        _openWebItem = new ToolStripMenuItem("🌐 操作盤を開く (Web UI)", null, (s, e) =>
        {
            OpenWebUi();
        });

        _exitItem = new ToolStripMenuItem("❌ 終了", null, (s, e) =>
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
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        // ダブルクリックで Web UI を開く
        _notifyIcon.DoubleClick += (s, e) => OpenWebUi();
        _notifyIcon.BalloonTipClicked += (s, e) => OpenWebUi();

        // ワークフローイベントの購読
        _workflow.RecordingStarted += sessionId =>
        {
            ShowBalloon("🎙️ 会議記録開始", "音声合成録音と画面変化監視を開始しました。", ToolTipIcon.Info);
        };

        _workflow.PipelineProgress += (step, msg) =>
        {
            // パイプライン進行中のステータス更新
        };

        _workflow.PipelineCompleted += msg =>
        {
            ShowBalloon("✅ 記録完了", msg, ToolTipIcon.Info);
        };

        _workflow.PipelineFailed += (step, err) =>
        {
            ShowBalloon("⚠️ 自動処理失敗", $"ステップ '{step}' でエラー: {err} (ローカルスプールは保護されています)", ToolTipIcon.Warning);
        };

        // 100ms周期の描画更新タイマー
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 100
        };
        _timer.Tick += (s, e) => UpdateIconAndState();
        _timer.Start();

        // 初回描画
        UpdateIconAndState();
    }

    /// <summary>
    /// アイコン描画とメニュー状態の更新
    /// </summary>
    private void UpdateIconAndState()
    {
        if (_isDisposed) return;

        _tickCount++;
        bool isRecording = _workflow.IsRecording;
        float audioLevel = _audioEngine.CurrentAudioLevel; // 0.0f ~ 1.0f

        // メニュー項目の状態更新
        _startItem.Enabled = !isRecording;
        _stopItem.Enabled = isRecording;
        _screenshotItem.Enabled = isRecording;

        if (isRecording)
        {
            var elapsed = _workflow.ElapsedTime;
            string timeStr = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            int levelPct = (int)(audioLevel * 100);

            _headerItem.Text = $"🔴 録音中 ({timeStr}) [音声: {levelPct}%]";
            string tooltip = $"WinMeetingRecorder (録音中: {timeStr} | 音声: {levelPct}%)";
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            _notifyIcon.Text = tooltip;
        }
        else
        {
            _headerItem.Text = "🎙️ WinMeetingRecorder (待機中)";
            _notifyIcon.Text = "WinMeetingRecorder (待機中)";
        }

        // 動的アイコンビットマップの生成 (32x32)
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            if (isRecording)
            {
                // --- 録音中デザイン ---
                // 背景: 角丸のダークスレート
                using (var bgBrush = new SolidBrush(Color.FromArgb(240, 15, 23, 42)))
                {
                    FillRoundedRectangle(g, bgBrush, new Rectangle(0, 0, 31, 31), 6);
                }

                // 左上: 録音中インジケーター (鮮やかな赤丸 ● REC)
                using (var recBrush = new SolidBrush(Color.FromArgb(239, 68, 68)))
                {
                    g.FillEllipse(recBrush, 3, 4, 7, 7);
                }

                // 音声波形バー (4本)
                bool hasVoice = audioLevel > 0.03f;
                Color barColor = hasVoice 
                    ? Color.FromArgb(34, 197, 94) // 鮮やかなエメラルドグリーン
                    : Color.FromArgb(100, 116, 139); // 落ち着いたスレートグレー

                int[] barX = [13, 17, 21, 25];
                float[] factors = [0.9f, 1.4f, 1.2f, 0.8f];
                int maxHeight = 20;
                int baseY = 26;

                using (var barBrush = new SolidBrush(barColor))
                {
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
                            float val = Math.Clamp(audioLevel * factor + jitter, 0.1f, 1.0f);
                            h = Math.Max(3, (int)(val * maxHeight));
                        }

                        int x = barX[i];
                        int y = baseY - h;
                        g.FillRectangle(barBrush, x, y, 3, h);
                    }
                }
            }
            else
            {
                // --- 待機中デザイン ---
                // 背景: 落ち着いたダークグレー
                using (var bgBrush = new SolidBrush(Color.FromArgb(230, 30, 41, 59)))
                {
                    FillRoundedRectangle(g, bgBrush, new Rectangle(0, 0, 31, 31), 6);
                }

                // マイクアイコン (スレートブルー / 白)
                using var micBrush = new SolidBrush(Color.FromArgb(148, 163, 184));
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

        // HICON の生成と安全なリソース破棄
        IntPtr hIcon = bmp.GetHicon();
        var oldHIcon = _lastHIcon;
        _lastHIcon = hIcon;
        _notifyIcon.Icon = Icon.FromHandle(hIcon);

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

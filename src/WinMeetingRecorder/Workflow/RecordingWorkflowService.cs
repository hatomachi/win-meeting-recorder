using System.Diagnostics;
using WinMeetingRecorder.Ai;
using WinMeetingRecorder.Audio;
using WinMeetingRecorder.Box;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.GitLab;
using WinMeetingRecorder.Logging;
using WinMeetingRecorder.Screen;
using WinMeetingRecorder.Spool;
using WinMeetingRecorder.Whisper;

namespace WinMeetingRecorder.Workflow;

public record RecordingStartResult(
    bool Success, 
    string Message, 
    string? SessionId = null, 
    string? SessionDir = null, 
    string? AudioPath = null, 
    bool ScreenCaptureEnabled = true);

public record RecordingStopResult(
    bool Success, 
    string Message, 
    string? SessionId = null, 
    string? AudioFile = null, 
    int CapturedCount = 0, 
    AudioConversionResult? Mp3Conversion = null, 
    WhisperTranscribeResult? WhisperTranscription = null, 
    AiExecutionResult? AiMinutes = null, 
    GitLabUploadResult? GitLabUpload = null, 
    BoxUploadResult? BoxUpload = null,
    PipelineStatus? Pipeline = null);

public record ActiveSessionState(string SessionId, string SpoolDirectory);

public record RecordStartOptions(
    string? PlaybackDeviceId = null, 
    string? CaptureDeviceId = null, 
    int? MonitorIndex = null, 
    bool? EnableScreenCapture = null,
    string? ThemeId = null);

public record RecordingHealthStatus(
    string OverallStatus,
    bool SleepPrevented,
    int AudioReconnectCount,
    int ScreenFallbackCount,
    int SilenceSeconds,
    string ScreenState,
    string ActivePlaybackDevice,
    string ActiveMicDevice,
    string? WarningMessage);

public record CurrentPipelineProgress(
    string Phase, // "idle", "recording", "converting", "uploading", "verifying", "completed", "error"
    string Message,
    int ProgressPercent,
    string? CurrentFile = null);

public class RecordingWorkflowService
{
    private readonly AudioEngine _audioEngine;
    private readonly ScreenCaptureEngine _screenEngine;
    private readonly SpoolService _spoolService;
    private readonly BoxService _boxService;
    private readonly GitLabService _gitLabService;
    private readonly WhisperService _whisperService;
    private readonly AiService _aiService;
    private readonly ConfigService _configService;

    private readonly object _lock = new();
    private ActiveSessionState? _activeSession;
    private DateTime? _recordStartTime;
    private string? _selectedThemeId;

    private CancellationTokenSource? _watchdogCts;
    private bool _silenceWarningNotified;
    private CurrentPipelineProgress _currentProgress = new("idle", "待機中", 0);

    public CurrentPipelineProgress CurrentProgress
    {
        get
        {
            lock (_lock)
            {
                return _currentProgress;
            }
        }
    }

    public void UpdateProgress(string phase, string message, int progressPercent, string? currentFile = null)
    {
        lock (_lock)
        {
            _currentProgress = new CurrentPipelineProgress(phase, message, progressPercent, currentFile);
        }
    }

    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _audioEngine.IsRecording || _screenEngine.IsCapturing;
            }
        }
    }

    public ActiveSessionState? ActiveSession
    {
        get
        {
            lock (_lock)
            {
                return _activeSession;
            }
        }
    }

    public string? SelectedThemeId
    {
        get
        {
            lock (_lock)
            {
                return _selectedThemeId;
            }
        }
    }

    public void UpdateSelectedTheme(string themeId)
    {
        lock (_lock)
        {
            _selectedThemeId = themeId;
            Console.WriteLine($"[Workflow] 録音セッションの選択テーマを更新しました: {themeId}");
        }
    }

    public TimeSpan ElapsedTime
    {
        get
        {
            lock (_lock)
            {
                if (_recordStartTime.HasValue && IsRecording)
                {
                    return DateTime.Now - _recordStartTime.Value;
                }
                return TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// 録音中に注意・警告状態（長時間無音、画面ロック中など）にあるかどうか
    /// </summary>
    public bool HasActiveWarning
    {
        get
        {
            if (!IsRecording) return false;
            var health = GetHealthStatus();
            return health.OverallStatus == "warning";
        }
    }

    // イベント通知
    public event Action<string>? RecordingStarted;
    public event Action<string>? RecordingStopped;
    public event Action<string, string>? PipelineProgress;
    public event Action<string>? PipelineCompleted;
    public event Action<string, string>? PipelineFailed;
    public event Action<string>? HealthNotice;
    public event Action<string>? HealthWarning;

    public RecordingWorkflowService(
        AudioEngine audioEngine,
        ScreenCaptureEngine screenEngine,
        SpoolService spoolService,
        BoxService boxService,
        GitLabService gitLabService,
        WhisperService whisperService,
        AiService aiService,
        ConfigService configService)
    {
        _audioEngine = audioEngine;
        _screenEngine = screenEngine;
        _spoolService = spoolService;
        _boxService = boxService;
        _gitLabService = gitLabService;
        _whisperService = whisperService;
        _aiService = aiService;
        _configService = configService;

        // 会議テーマ切り替えイベントの連携
        _boxService.ThemeSelected += themeId =>
        {
            UpdateSelectedTheme(themeId);
        };

        // 画面キャプチャイベントの連携
        _screenEngine.OnCaptured += evt =>
        {
            lock (_lock)
            {
                if (_activeSession != null)
                {
                    _spoolService.RecordImageCaptured(_activeSession.SessionId, evt);
                }
            }
        };

        // 自己修復・自動フォールバック通知の連携
        _audioEngine.OnDeviceReconnected += msg =>
        {
            HealthNotice?.Invoke(msg);
        };

        _screenEngine.OnScreenStatusMessage += msg =>
        {
            HealthNotice?.Invoke(msg);
        };
    }

    /// <summary>
    /// 現在の健全性・死活監視ステータスを取得する
    /// </summary>
    public RecordingHealthStatus GetHealthStatus()
    {
        if (!IsRecording)
        {
            return new RecordingHealthStatus(
                "idle",
                PowerManagementService.IsSleepPrevented,
                _audioEngine.ReconnectCount,
                _screenEngine.FallbackCount,
                0,
                "idle",
                _audioEngine.ActivePlaybackDeviceName,
                _audioEngine.ActiveMicDeviceName,
                null);
        }

        var nowUtc = DateTime.UtcNow;
        int silenceSec = _audioEngine.LastNonSilentUtc > DateTime.MinValue
            ? Math.Max(0, (int)(nowUtc - _audioEngine.LastNonSilentUtc).TotalSeconds)
            : 0;

        string? warningMsg = null;
        string overall = "healthy";

        if (_screenEngine.HealthStatus == "screen_locked")
        {
            overall = "warning";
            warningMsg = _screenEngine.LastWarningMessage ?? "画面ロック中 (解除後に自動再開します)";
        }
        else if (silenceSec >= 180)
        {
            overall = "warning";
            warningMsg = $"約{silenceSec / 60}分間、音声入力が検出されていません (ミュート確認)";
        }
        else if (!string.IsNullOrEmpty(_audioEngine.LastWarningMessage))
        {
            overall = "warning";
            warningMsg = _audioEngine.LastWarningMessage;
        }

        return new RecordingHealthStatus(
            overall,
            PowerManagementService.IsSleepPrevented,
            _audioEngine.ReconnectCount,
            _screenEngine.FallbackCount,
            silenceSec,
            _screenEngine.HealthStatus,
            _audioEngine.ActivePlaybackDeviceName,
            _audioEngine.ActiveMicDeviceName,
            warningMsg);
    }

    /// <summary>
    /// 記録を開始する (Web API および タスクトレイ共通)
    /// </summary>
    public Task<RecordingStartResult> StartRecordingAsync(RecordStartOptions? options = null)
    {
        lock (_lock)
        {
            if (_audioEngine.IsRecording || _screenEngine.IsCapturing)
            {
                return Task.FromResult(new RecordingStartResult(false, "既に記録中です。"));
            }

            int monitorIdx = options?.MonitorIndex ?? 0;
            var screenInfo = ScreenService.ResolveScreen(monitorIdx);

            _selectedThemeId = options?.ThemeId ?? _configService.LoadConfig().Box.SelectedThemeId;

            // スプールディレクトリとメタデータの初期化
            var session = _spoolService.CreateSession(monitorIdx, screenInfo.Name);
            _activeSession = new ActiveSessionState(session.SessionId, session.SpoolDirectory);
            _recordStartTime = DateTime.Now;
            _silenceWarningNotified = false;

            var outputPath = Path.Combine(session.SpoolDirectory, "meeting_audio.wav");

            try
            {
                // 0. OSスリープ・画面消灯の完全抑止
                PowerManagementService.PreventSleepAndDisplayOff();

                // 1. 音声録音開始
                _audioEngine.StartRecording(outputPath, options?.PlaybackDeviceId, options?.CaptureDeviceId);

                // 2. 画面キャプチャ開始 (デフォルト有効)
                bool enableScreen = options?.EnableScreenCapture ?? true;
                if (enableScreen)
                {
                    var opt = new ScreenCaptureOptions
                    {
                        MonitorIndex = monitorIdx
                    };
                    _screenEngine.StartCapture(session.SpoolDirectory, opt);
                }

                // 3. 死活監視 Watchdog 開始
                StartWatchdogLoop();

                UpdateProgress("recording", "記録中", 0);

                var themeName = _configService.LoadConfig().Box.ThemesCache.FirstOrDefault(t => t.Id == _selectedThemeId)?.Name ?? _selectedThemeId ?? "既定";
                AppLogger.Info($"🎙️ 会議記録を開始しました (テーマ: {themeName} / セッション: {session.SessionId})");
                RecordingStarted?.Invoke(session.SessionId);

                return Task.FromResult(new RecordingStartResult(
                    true, 
                    "記録を開始しました。", 
                    session.SessionId, 
                    session.SpoolDirectory, 
                    outputPath, 
                    enableScreen));
            }
            catch (Exception ex)
            {
                // ロールバック
                StopWatchdogLoop();
                PowerManagementService.RestorePowerSettings();

                if (_audioEngine.IsRecording)
                {
                    _ = _audioEngine.StopRecordingAsync();
                }
                if (_screenEngine.IsCapturing)
                {
                    _ = _screenEngine.StopCaptureAsync();
                }
                _activeSession = null;
                _recordStartTime = null;
                return Task.FromResult(new RecordingStartResult(false, $"記録開始に失敗しました: {ex.Message}"));
            }
        }
    }

    /// <summary>
    /// 録音中の死活監視 (Watchdog) ループを開始する
    /// </summary>
    private void StartWatchdogLoop()
    {
        StopWatchdogLoop();
        _watchdogCts = new CancellationTokenSource();
        var token = _watchdogCts.Token;

        Task.Run(async () =>
        {
            int tick = 0;
            while (!token.IsCancellationRequested && IsRecording)
            {
                try
                {
                    await Task.Delay(3000, token);
                    if (!IsRecording) break;
                    tick++;

                    // 1. 30秒ごとにOSのスリープ・画面OFF抑止タイマーを再パルス
                    if (tick % 10 == 0)
                    {
                        PowerManagementService.PulseKeepAlive();
                    }

                    var nowUtc = DateTime.UtcNow;

                    // 2. AudioEngine の書き込みストール監視 (6秒以上書き込みが止まっていたらデバイス再初期化を要求)
                    if (_audioEngine.IsRecording && _audioEngine.LastAudioWriteUtc > DateTime.MinValue)
                    {
                        double stalledSec = (nowUtc - _audioEngine.LastAudioWriteUtc).TotalSeconds;
                        if (stalledSec > 6.0)
                        {
                            Console.WriteLine($"[Watchdog] ⚠️ 音声書き込みの遅延({stalledSec:F1}秒)を検知しました。デバイス再接続を要求します。");
                            _audioEngine.RequestDeviceRefresh();
                        }
                    }

                    // 3. 長時間完全無音 (3分 = 180秒以上) の検知と通知
                    if (_audioEngine.IsRecording && _audioEngine.LastNonSilentUtc > DateTime.MinValue)
                    {
                        double silenceSec = (nowUtc - _audioEngine.LastNonSilentUtc).TotalSeconds;
                        if (silenceSec >= 180.0 && !_silenceWarningNotified)
                        {
                            _silenceWarningNotified = true;
                            string warn = "3分間、音声が検出されていません。マイクのミュートやデバイス設定を確認してください。";
                            Console.WriteLine($"[Watchdog] ⚠️ {warn}");
                            HealthWarning?.Invoke(warn);
                        }
                        else if (silenceSec < 5.0 && _silenceWarningNotified)
                        {
                            // 再び音声が入ったら警告フラグをクリア
                            _silenceWarningNotified = false;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Watchdog] 監視ループ例外: {ex.Message}");
                }
            }
        }, token);
    }

    private void StopWatchdogLoop()
    {
        try
        {
            _watchdogCts?.Cancel();
            _watchdogCts?.Dispose();
        }
        catch { /* ignore */ }
        finally
        {
            _watchdogCts = null;
        }
    }

    /// <summary>
    /// 手動スクリーンショットを撮影する
    /// </summary>
    public ScreenCaptureEvent? CaptureManual()
    {
        lock (_lock)
        {
            if (!_screenEngine.IsCapturing)
            {
                return null;
            }
            var ev = _screenEngine.CaptureManual();
            if (ev != null)
            {
                AppLogger.Info($"📸 手動スクリーンショットを撮影しました (通算 {_screenEngine.CapturedCount} 枚目)");
            }
            return ev;
        }
    }

    /// <summary>
    /// 記録を停止し、自動パイプライン (MP3化 -> Boxアップロード) を実行する
    /// </summary>
    public async Task<RecordingStopResult> StopRecordingAsync(bool? autoUpload = null, string? overrideThemeId = null)
    {
        string? stoppingSessionId;
        string? stoppingSessionDir;

        lock (_lock)
        {
            if (!_audioEngine.IsRecording && !_screenEngine.IsCapturing && _activeSession == null)
            {
                return new RecordingStopResult(false, "記録中ではありません。");
            }

            stoppingSessionId = _activeSession?.SessionId;
            stoppingSessionDir = _activeSession?.SpoolDirectory;
            _activeSession = null;
            _recordStartTime = null;
        }

        StopWatchdogLoop();
        PowerManagementService.RestorePowerSettings();

        try
        {
            if (_screenEngine.IsCapturing)
            {
                await _screenEngine.StopCaptureAsync();
            }

            if (_audioEngine.IsRecording)
            {
                await _audioEngine.StopRecordingAsync();
            }

            if (stoppingSessionId != null)
            {
                AppLogger.Info($"⏹️ 会議記録を停止しました (セッション: {stoppingSessionId} / スクショ: {_screenEngine.CapturedCount}枚)");
                RecordingStopped?.Invoke(stoppingSessionId);
            }

            AudioConversionResult? mp3Result = null;
            string? targetAudioPath = null;

            // 1. WAV -> MP3 自動軽量化 (24kHz Mono 64kbps)
            if (!string.IsNullOrEmpty(stoppingSessionDir))
            {
                var wavPath = Path.Combine(stoppingSessionDir, "meeting_audio.wav");
                var mp3Path = Path.Combine(stoppingSessionDir, "meeting_audio.mp3");

                if (File.Exists(wavPath))
                {
                    UpdateProgress("converting", "WAV音声をMP3へ高圧縮中...", 20);
                    PipelineProgress?.Invoke("audio", "WAV音声をMP3へ高圧縮中...");
                    mp3Result = AudioConverter.ConvertWavToMp3(wavPath, mp3Path, targetSampleRate: 24000, targetChannels: 1, desiredBitRate: 64000);
                    targetAudioPath = mp3Result.Success ? mp3Path : wavPath;
                    if (mp3Result.Success)
                    {
                        AppLogger.Info($"🎵 WAV音声をMP3へ高圧縮完了: {mp3Result.CompressedSizeBytes / 1024.0:F1} KB (圧縮率 {mp3Result.CompressionRatioPercent:F1}%)");
                    }
                }
            }

            // 2. メタデータの確定
            MeetingSessionMetadata? metadata = null;
            if (!string.IsNullOrEmpty(stoppingSessionDir))
            {
                metadata = _spoolService.FinishSession(stoppingSessionDir);
                if (metadata != null)
                {
                    metadata.Pipeline.Audio = "success";
                    metadata.Pipeline.LastStep = "audio";
                    _spoolService.UpdateMetadata(metadata);
                }
            }

            var appConfig = _configService.LoadConfig();

            // 3. テーマの解決 (明示指定 > セッション最新選択 > 設定ファイルの既定)
            string? effectiveThemeId = !string.IsNullOrWhiteSpace(overrideThemeId) 
                ? overrideThemeId 
                : (_selectedThemeId ?? appConfig.Box.SelectedThemeId);

            // 4. Box アップロード判定
            bool shouldUpload = autoUpload ?? appConfig.Box.AutoUploadOnStop;
            BoxUploadResult? boxResult = null;

            if (shouldUpload && appConfig.Box.IsAuthenticated && !string.IsNullOrEmpty(stoppingSessionDir))
            {
                if (metadata != null)
                {
                    metadata.Pipeline.Upload = "running";
                    metadata.Pipeline.LastStep = "upload";
                    _spoolService.UpdateMetadata(metadata);
                }

                UpdateProgress("uploading", "Boxへアップロード準備中...", 40);
                PipelineProgress?.Invoke("upload", "Boxへ会議記録の一括アップロードを開始します...");
                boxResult = await _boxService.UploadSessionAsync(
                    stoppingSessionDir, 
                    metadata!, 
                    effectiveThemeId, 
                    onProgress: (cur, total, file) =>
                    {
                        int pct = 40 + (int)((double)cur / Math.Max(1, total) * 55);
                        string msg = file == "meta.json"
                            ? "⭐ meta.json (完了トリガー) をアップロード中..."
                            : $"Boxへアップロード中: {cur}/{total} ({file})";
                        UpdateProgress("uploading", msg, pct, file);
                        PipelineProgress?.Invoke("upload", msg);
                    });

                if (boxResult.Success && metadata != null)
                {
                    metadata.Pipeline.Upload = "success";
                    UpdateProgress("completed", "✅ Boxへの一括アップロード完了！", 100);
                    PipelineCompleted?.Invoke("会議記録のBoxへのアップロードが完了しました！");
                }
                else if (metadata != null)
                {
                    metadata.Pipeline.Upload = "error";
                    metadata.Pipeline.ErrorMessage = boxResult.Message;
                    _spoolService.UpdateMetadata(metadata);
                    UpdateProgress("error", $"⚠️ Boxアップロードエラー: {boxResult.Message}", 0);
                    PipelineFailed?.Invoke("upload", boxResult.Message);
                }
            }
            else if (metadata != null)
            {
                metadata.Pipeline.Upload = "skipped";
                _spoolService.UpdateMetadata(metadata);
                UpdateProgress("completed", "会議記録をローカルに保管しました（アップロード保留）。", 100);
                PipelineCompleted?.Invoke("会議記録のローカル保存が完了しました。");
                AppLogger.Info($"📁 会議記録をローカルに保管しました (未送信スプールに保持 / セッション: {stoppingSessionId})");
            }

            return new RecordingStopResult(
                true,
                "記録を停止・保存しました。",
                stoppingSessionId,
                _audioEngine.CurrentOutputFile,
                _screenEngine.CapturedCount,
                mp3Result,
                null,
                null,
                null,
                boxResult,
                metadata?.Pipeline);
        }
        catch (Exception ex)
        {
            UpdateProgress("error", $"❌ 停止処理エラー: {ex.Message}", 0);
            PipelineFailed?.Invoke("error", ex.Message);
            return new RecordingStopResult(false, $"停止処理に失敗しました: {ex.Message}");
        }
    }
}

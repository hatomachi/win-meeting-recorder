using System.Diagnostics;
using WinMeetingRecorder.Ai;
using WinMeetingRecorder.Audio;
using WinMeetingRecorder.Box;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.GitLab;
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

    // イベント通知
    public event Action<string>? RecordingStarted;
    public event Action<string>? RecordingStopped;
    public event Action<string, string>? PipelineProgress;
    public event Action<string>? PipelineCompleted;
    public event Action<string, string>? PipelineFailed;

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

            var outputPath = Path.Combine(session.SpoolDirectory, "meeting_audio.wav");

            try
            {
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

                Console.WriteLine($"[Workflow] 🎙️ 会議記録を開始しました: セッション {session.SessionId}");
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
            return _screenEngine.CaptureManual();
        }
    }

    /// <summary>
    /// 記録を停止し、自動パイプライン (MP3化 -> Whisper文字起こし -> AI議事録 -> GitLabプッシュ) を実行する
    /// </summary>
    public async Task<RecordingStopResult> StopRecordingAsync()
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
                    PipelineProgress?.Invoke("audio", "WAV音声をMP3へ高圧縮中...");
                    mp3Result = AudioConverter.ConvertWavToMp3(wavPath, mp3Path, targetSampleRate: 24000, targetChannels: 1, desiredBitRate: 64000);
                    targetAudioPath = mp3Result.Success ? mp3Path : wavPath;
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

            // 3. Box 自動一括アップロード (メインパイプライン)
            BoxUploadResult? boxResult = null;
            if (appConfig.Box.AutoUploadOnStop && appConfig.Box.IsAuthenticated && !string.IsNullOrEmpty(stoppingSessionDir))
            {
                if (metadata != null)
                {
                    metadata.Pipeline.Upload = "running";
                    metadata.Pipeline.LastStep = "upload";
                    _spoolService.UpdateMetadata(metadata);
                }

                PipelineProgress?.Invoke("upload", "Boxへ会議記録の一括アップロードを開始します...");
                boxResult = await _boxService.UploadSessionAsync(
                    stoppingSessionDir, 
                    metadata!, 
                    _selectedThemeId, 
                    onProgress: (cur, total, file) =>
                    {
                        PipelineProgress?.Invoke("upload", $"Boxへアップロード中: {cur}/{total} ({file})");
                    });

                if (boxResult.Success && metadata != null)
                {
                    metadata.Pipeline.Upload = "success";
                    PipelineCompleted?.Invoke("会議記録のBoxへのアップロードが完了しました！");
                }
                else if (metadata != null)
                {
                    metadata.Pipeline.Upload = "error";
                    metadata.Pipeline.ErrorMessage = boxResult.Message;
                    _spoolService.UpdateMetadata(metadata);
                    PipelineFailed?.Invoke("upload", boxResult.Message);
                }
            }
            else if (metadata != null)
            {
                metadata.Pipeline.Upload = "skipped";
                _spoolService.UpdateMetadata(metadata);
                PipelineCompleted?.Invoke("会議記録のローカル保存が完了しました。");
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
            PipelineFailed?.Invoke("error", ex.Message);
            return new RecordingStopResult(false, $"停止処理に失敗しました: {ex.Message}");
        }
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using WinMeetingRecorder.Audio;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.GitLab;
using WinMeetingRecorder.Screen;
using WinMeetingRecorder.Spool;
using WinMeetingRecorder.Ui;
using WinMeetingRecorder.Whisper;

var builder = WebApplication.CreateBuilder(args);

// DI コンテナ登録
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<SpoolService>();
builder.Services.AddSingleton<GitLabService>();
builder.Services.AddSingleton<WhisperService>();
builder.Services.AddSingleton<AudioEngine>();
builder.Services.AddSingleton<ScreenCaptureEngine>();

// アクティブセッション管理 (メモリ保持)
ActiveSessionState? activeSession = null;
var sessionLock = new object();

// ブラウザ自動オープン抑制フラグ判定
bool noBrowser = args.Any(a => a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase) || a.Equals("-nb", StringComparison.OrdinalIgnoreCase));
var nonFlagArgs = args.Where(a => !a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase) && !a.Equals("-nb", StringComparison.OrdinalIgnoreCase)).ToArray();

// CLIモード判定
if (nonFlagArgs.Length > 0)
{
    var command = nonFlagArgs[0].ToLowerInvariant();

    if (command == "--list-devices" || command == "-l")
    {
        Console.WriteLine("========================================");
        Console.WriteLine(" 🎙️ 利用可能なオーディオデバイス一覧");
        Console.WriteLine("========================================");
        
        Console.WriteLine("\n[再生デバイス (スピーカー / Loopback対象)]");
        var playbacks = DeviceService.GetPlaybackDevices();
        for (int i = 0; i < playbacks.Count; i++)
        {
            var d = playbacks[i];
            Console.WriteLine($"  [{i + 1}] {d.Name} {(d.IsDefault ? "(★既定)" : "")}");
            Console.WriteLine($"      ID: {d.Id}");
        }

        Console.WriteLine("\n[録音デバイス (マイク入力)]");
        var captures = DeviceService.GetCaptureDevices();
        for (int i = 0; i < captures.Count; i++)
        {
            var d = captures[i];
            Console.WriteLine($"  [{i + 1}] {d.Name} {(d.IsDefault ? "(★既定)" : "")}");
            Console.WriteLine($"      ID: {d.Id}");
        }

        Console.WriteLine("\n========================================");
        return;
    }

    if (command == "--list-screens" || command == "-ls")
    {
        Console.WriteLine("========================================");
        Console.WriteLine(" 🖥️ 利用可能なディスプレイ一覧 (Phase 2)");
        Console.WriteLine("========================================");
        
        var screens = ScreenService.GetScreens();
        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            Console.WriteLine($"  [{s.Index}] {s.Name} {(s.IsPrimary ? "(★既定/プライマリ)" : "")}");
            Console.WriteLine($"      解像度: {s.Bounds.Width}x{s.Bounds.Height} at ({s.Bounds.X}, {s.Bounds.Y})");
        }

        var virtualDesk = ScreenService.GetVirtualDesktop();
        Console.WriteLine($"\n  [-1] {virtualDesk.Name}");
        Console.WriteLine($"      解像度: {virtualDesk.Bounds.Width}x{virtualDesk.Bounds.Height} at ({virtualDesk.Bounds.X}, {virtualDesk.Bounds.Y})");

        Console.WriteLine("\n========================================");
        return;
    }

    if (command == "--test-audio" || command == "-t")
    {
        int durationSec = 10;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedSec))
        {
            durationSec = parsedSec;
        }

        string outputFile = args.Length > 2 ? args[2] : "test_mixed.wav";
        outputFile = Path.GetFullPath(outputFile);

        Console.WriteLine("========================================");
        Console.WriteLine(" 🎙️ 音声合成テスト録音モード (Phase 1)");
        Console.WriteLine("========================================");
        Console.WriteLine($"録音時間目標: {durationSec} 秒");
        Console.WriteLine($"出力ファイル: {outputFile}");
        Console.WriteLine("スピーカーから音楽や動画を流しつつ、マイクで話してください...");
        Console.WriteLine("----------------------------------------");

        using var engine = new AudioEngine();
        try
        {
            engine.StartRecording(outputFile);
            Console.WriteLine("🔴 録音開始中...");

            for (int i = durationSec; i > 0; i--)
            {
                Console.Write($"\r残り時間: {i} 秒   ");
                Thread.Sleep(1000);
            }
            Console.WriteLine();

            Console.WriteLine("⏹️ 録音停止中 & ファイル確定中...");
            engine.StopRecordingAsync().GetAwaiter().GetResult();

            var fileInfo = new FileInfo(outputFile);
            Console.WriteLine($"✅ 録音完了！");
            Console.WriteLine($"   ファイルサイズ: {fileInfo.Length / 1024.0:F1} KB");
            Console.WriteLine($"   パス: {outputFile}");

            try
            {
                using var reader = new AudioFileReader(outputFile);
                Console.WriteLine($"   実録音時間: {reader.TotalTime.TotalSeconds:F2} 秒 (目標: {durationSec} 秒)");
            }
            catch { /* ignore */ }

            Console.WriteLine("手元Windows機でこのWAVファイルを再生し、音割れやピッチ異常がないか確認してください。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラーが発生しました: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        return;
    }

    if (command == "--test-mp3")
    {
        string inputWav = args.Length > 1 ? args[1] : "test_mixed.wav";
        string outputMp3 = args.Length > 2 ? args[2] : Path.ChangeExtension(inputWav, ".mp3");
        inputWav = Path.GetFullPath(inputWav);
        outputMp3 = Path.GetFullPath(outputMp3);

        Console.WriteLine("========================================");
        Console.WriteLine(" 🎵 音声MP3変換テスト (Phase 6A)");
        Console.WriteLine("========================================");
        Console.WriteLine($"入力WAV: {inputWav}");
        Console.WriteLine($"出力MP3: {outputMp3}");
        Console.WriteLine("----------------------------------------");

        AudioConverter.ListSupportedMp3Formats();
        Console.WriteLine("----------------------------------------");
        Console.WriteLine("MP3エンコード実行中 (24kHz Mono 64kbps)...");

        var result = AudioConverter.ConvertWavToMp3(inputWav, outputMp3, targetSampleRate: 24000, targetChannels: 1, desiredBitRate: 64000);
        if (result.Success)
        {
            Console.WriteLine("✅ MP3変換成功！");
            Console.WriteLine($"   元サイズ: {result.OriginalSizeBytes / 1024.0:F1} KB");
            Console.WriteLine($"   圧縮後:   {result.CompressedSizeBytes / 1024.0:F1} KB");
            Console.WriteLine($"   削減率:   {result.CompressionRatioPercent:F1} %");
            Console.WriteLine($"   所要時間: {result.ElapsedMilliseconds} ms");
            Console.WriteLine($"   保存先:   {result.OutputFilePath}");
        }
        else
        {
            Console.WriteLine($"❌ MP3変換失敗: {result.ErrorMessage}");
        }
        Console.WriteLine("========================================");
        return;
    }

    if (command == "--test-screen" || command == "-ts")
    {
        int durationSec = 15;
        if (args.Length > 1 && int.TryParse(args[1], out var parsedSec))
        {
            durationSec = parsedSec;
        }

        int monitorIndex = 0;
        if (args.Length > 2 && int.TryParse(args[2], out var parsedMonitor))
        {
            monitorIndex = parsedMonitor;
        }

        string outputDir = args.Length > 3 ? args[3] : Path.Combine(Directory.GetCurrentDirectory(), "test_screens");
        outputDir = Path.GetFullPath(outputDir);

        Console.WriteLine("========================================");
        Console.WriteLine(" 🖥️ 画面キャプチャ＆変化検知テスト (Phase 2)");
        Console.WriteLine("========================================");
        Console.WriteLine($"監視時間: {durationSec} 秒");
        var targetScreen = ScreenService.ResolveScreen(monitorIndex);
        Console.WriteLine($"対象モニタ: [{targetScreen.Index}] {targetScreen.Name} ({targetScreen.Bounds.Width}x{targetScreen.Bounds.Height})");
        Console.WriteLine($"出力先: {outputDir}");
        Console.WriteLine("画面上でウィンドウを切り替えたり、スライドを進めたりしてください...");
        Console.WriteLine("----------------------------------------");

        using var engine = new ScreenCaptureEngine();
        engine.OnCaptured += (evt) =>
        {
            Console.WriteLine($"\n📸 [{evt.Reason}] 画面保存: {evt.FileName} (差分: {evt.Diff * 100:F1}%, 経過: {evt.ElapsedSeconds}秒)");
        };
        engine.OnDiffCalculated += (diff, elapsed) =>
        {
            Console.Write($"\r[監視中] 経過: {elapsed}/{durationSec}秒 | 画面差分: {diff * 100:F1}%   ");
        };

        try
        {
            var options = new ScreenCaptureOptions
            {
                MonitorIndex = monitorIndex,
                DiffThreshold = 0.05,
                MinIntervalSec = 2,
                MaxIntervalSec = 60,
                CheckIntervalMs = 1000,
                DebounceStabilityMs = 500,
                JpegQuality = 80L
            };

            engine.StartCapture(outputDir, options);

            for (int i = 0; i < durationSec; i++)
            {
                Thread.Sleep(1000);
            }
            Console.WriteLine();

            Console.WriteLine("⏹️ 監視停止中...");
            engine.StopCaptureAsync().GetAwaiter().GetResult();

            Console.WriteLine("========================================");
            Console.WriteLine("✅ 画面キャプチャテスト完了！");
            Console.WriteLine($"   撮影枚数: {engine.CapturedCount} 枚");
            Console.WriteLine($"   保存先ディレクトリ: {Path.Combine(outputDir, "images")}");
            Console.WriteLine("保存されたJPEG画像を開き、スライド変化検知や画質（品質80%）を確認してください。");
            Console.WriteLine("========================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラーが発生しました: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        return;
    }

    if (command == "--test-gitlab")
    {
        var configService = new ConfigService();
        var gitLabService = new GitLabService();
        var config = configService.LoadConfig().GitLab;

        if (args.Length > 3)
        {
            config.ServerUrl = args[1];
            config.ProjectId = args[2];
            config.PersonalAccessToken = args[3];
        }

        Console.WriteLine("========================================");
        Console.WriteLine(" 🦊 GitLab API 接続テスト (Phase 4)");
        Console.WriteLine("========================================");
        Console.WriteLine($"URL: {config.ServerUrl}");
        Console.WriteLine($"Project: {config.ProjectId}");
        Console.WriteLine($"Token: {(string.IsNullOrEmpty(config.PersonalAccessToken) ? "(未設定)" : "******")}");
        Console.WriteLine("----------------------------------------");

        var testResult = gitLabService.TestConnectionAsync(config).GetAwaiter().GetResult();
        if (testResult.Success)
        {
            Console.WriteLine($"✅ {testResult.Message}");
            Console.WriteLine($"   プロジェクト名: {testResult.ProjectName}");
            Console.WriteLine($"   デフォルトブランチ: {testResult.DefaultBranch}");
            Console.WriteLine($"   WebURL: {testResult.WebUrl}");
        }
        else
        {
            Console.WriteLine($"❌ {testResult.Message}");
        }
        Console.WriteLine("========================================");
        return;
    }

    if (command == "--test-whisper")
    {
        var configService = new ConfigService();
        var whisperService = new WhisperService();
        var config = configService.LoadConfig().Whisper;

        if (args.Length > 1) config.BaseUrl = args[1];
        string audioFile = args.Length > 2 ? args[2] : "test_3s.mp3";
        if (args.Length > 3) config.ApiKey = args[3];

        audioFile = Path.GetFullPath(audioFile);

        Console.WriteLine("========================================");
        Console.WriteLine(" 🎙️ OpenAI Whisper API 文字起こしテスト (Phase 6B)");
        Console.WriteLine("========================================");
        Console.WriteLine($"Base URL: {config.BaseUrl}");
        Console.WriteLine($"音声ファイル: {audioFile}");
        Console.WriteLine($"モデル: {config.Model}");
        Console.WriteLine($"言語: {config.Language}");
        Console.WriteLine("----------------------------------------");

        // 疎通確認
        Console.WriteLine("1. サーバー疎通テスト実行中...");
        var testResult = whisperService.TestConnectionAsync(config).GetAwaiter().GetResult();
        if (testResult.Success)
        {
            Console.WriteLine($"✅ {testResult.Message}");
        }
        else
        {
            Console.WriteLine($"⚠️ 疎通警告: {testResult.Message}");
        }

        // 音声文字起こし実行
        if (File.Exists(audioFile))
        {
            Console.WriteLine("\n2. 音声文字起こし (POST /v1/audio/transcriptions) 送信中...");
            var trResult = whisperService.TranscribeAudioAsync(audioFile, config).GetAwaiter().GetResult();
            if (trResult.Success)
            {
                Console.WriteLine($"✅ 文字起こし成功！");
                Console.WriteLine($"   セグメント数: {trResult.SegmentCount}");
                Console.WriteLine($"   所要時間:     {trResult.ElapsedMilliseconds} ms");
                Console.WriteLine($"   保存先:       {trResult.TranscriptFilePath}");
                Console.WriteLine("\n[認識テキスト全文]");
                Console.WriteLine(trResult.Text);

                if (trResult.Response?.Segments.Count > 0)
                {
                    Console.WriteLine("\n[各セグメント]");
                    foreach (var s in trResult.Response.Segments)
                    {
                        Console.WriteLine($"  [{s.Start:F1}s - {s.End:F1}s] {s.Text}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"❌ 文字起こし失敗: {trResult.Message}");
            }
        }
        else
        {
            Console.WriteLine($"⚠️ 指定された音声ファイルが見つからないため、文字起こし実行はスキップしました: {audioFile}");
        }

        Console.WriteLine("========================================");
        return;
    }

    if (command == "--pending-spool")
    {
        var spool = new SpoolService();
        var pendings = spool.GetPendingSessions();
        Console.WriteLine("========================================");
        Console.WriteLine($" 📁 保管中の未送信スプール一覧 ({pendings.Count} 件)");
        Console.WriteLine("========================================");
        foreach (var p in pendings)
        {
            Console.WriteLine($"  [{p.SessionId}] {p.CreatedAt:yyyy-MM-dd HH:mm:ss} | スクショ: {p.ScreenshotCount}枚 | 音声: {(p.HasAudio ? "あり" : "なし")} | MP3: {(p.HasMp3 ? "あり" : "なし")} | 文字起こし: {(p.HasTranscript ? "済" : "未")} | サイズ: {p.TotalSizeBytes / (1024.0 * 1024.0):F1} MB");
            Console.WriteLine($"      パス: {p.DirectoryPath}");
        }
        Console.WriteLine("========================================");
        return;
    }

    if (command == "--help" || command == "-h")
    {
        Console.WriteLine("========================================");
        Console.WriteLine(" 🎙️ WinMeetingRecorder ヘルプ");
        Console.WriteLine("========================================");
        Console.WriteLine("引数なしで実行すると、Web UI (http://0.0.0.0:5000) が起動します。\n");
        Console.WriteLine("CLIコマンド一覧:");
        Console.WriteLine("  --list-devices, -l             : オーディオ入出力デバイス一覧を表示");
        Console.WriteLine("  --list-screens, -ls            : ディスプレイ一覧を表示");
        Console.WriteLine("  --test-audio, -t [秒数] [出力パス] : 音声合成（相手の声＋マイク）テスト録音");
        Console.WriteLine("  --test-mp3 [WAVパス] [MP3パス] : 音声MP3軽量化 (24kHz Mono 64kbps) テスト");
        Console.WriteLine("  --test-screen, -ts [秒数] [モニタIndex] [出力先] : 画面変化検知スクショテスト");
        Console.WriteLine("  --test-gitlab [URL] [Project] [PAT] : GitLab REST API 接続テスト");
        Console.WriteLine("  --test-whisper [BaseUrl] [Audio] [Key] : Whisper API 文字起こしテスト");
        Console.WriteLine("  --pending-spool                : ローカルスプールに残っている未送信一覧");
        Console.WriteLine("  --no-browser, -nb              : 起動時に既定ブラウザを自動で開かない");
        Console.WriteLine("  --help, -h                     : このヘルプを表示");
        Console.WriteLine("========================================");
        return;
    }
}

// ==========================================
// ASP.NET Core Minimal API サーバー (Web UI)
// ==========================================
var app = builder.Build();

// スクショ撮影イベントの自動ハンドリング (スプールメタデータへの追記)
var screenCaptureEngine = app.Services.GetRequiredService<ScreenCaptureEngine>();
var spoolService = app.Services.GetRequiredService<SpoolService>();

screenCaptureEngine.OnCaptured += (evt) =>
{
    lock (sessionLock)
    {
        if (activeSession != null)
        {
            spoolService.RecordImageCaptured(activeSession.SessionId, evt);
        }
    }
};

// 静的ファイル配信 (フォールバック用)
app.UseDefaultFiles();
app.UseStaticFiles();

// Web UI: 単一exe内蔵HTML配信 (404防止・単一exeポータブル対応)
app.MapGet("/", () => Results.Content(IndexHtml.Content, "text/html; charset=utf-8"));
app.MapGet("/index.html", () => Results.Content(IndexHtml.Content, "text/html; charset=utf-8"));

// API: 設定の取得
app.MapGet("/api/config", (ConfigService configService) =>
{
    return Results.Ok(configService.LoadConfig());
});

// API: 設定の保存
app.MapPost("/api/config", (ConfigService configService, AppConfig newConfig) =>
{
    configService.SaveConfig(newConfig);
    return Results.Ok(new { message = "設定を保存しました。" });
});

// API: GitLab 接続テスト
app.MapPost("/api/gitlab/test", async (GitLabService gitLabService, GitLabConfig config) =>
{
    var result = await gitLabService.TestConnectionAsync(config);
    return Results.Ok(result);
});

// API: Whisper 接続テスト
app.MapPost("/api/whisper/test", async (WhisperService whisperService, WhisperConfig config) =>
{
    var result = await whisperService.TestConnectionAsync(config);
    return Results.Ok(result);
});

// API: 指定セッションを手動で Whisper 文字起こし実行
app.MapPost("/api/whisper/transcribe", async (
    SessionTranscribeRequest req,
    SpoolService spool,
    WhisperService whisperService,
    ConfigService configService) =>
{
    if (string.IsNullOrWhiteSpace(req.SessionId))
    {
        return Results.BadRequest(new { error = "セッションIDが指定されていません。" });
    }

    var config = configService.LoadConfig().Whisper;
    if (!config.IsConfigured)
    {
        return Results.BadRequest(new { error = "Whisperの設定が未完了です。先にBase URLを設定してください。" });
    }

    var sessionDir = Path.Combine(spool.SpoolBaseDir, req.SessionId);
    if (!Directory.Exists(sessionDir))
    {
        return Results.NotFound(new { error = $"指定されたセッションが見つかりません: {req.SessionId}" });
    }

    var metadata = spool.GetSessionMetadata(sessionDir);
    // 音声ファイルの特定 (MP3優先、なければWAVからMP3変換、それもなければWAV)
    var mp3Path = Path.Combine(sessionDir, "meeting_audio.mp3");
    var wavPath = Path.Combine(sessionDir, "meeting_audio.wav");

    string targetAudio;
    if (File.Exists(mp3Path))
    {
        targetAudio = mp3Path;
    }
    else if (File.Exists(wavPath))
    {
        // MP3 がまだ無ければ自動変換
        var conv = AudioConverter.ConvertWavToMp3(wavPath, mp3Path);
        targetAudio = conv.Success ? mp3Path : wavPath;
    }
    else
    {
        return Results.BadRequest(new { error = "セッション内に音声ファイルが存在しません。" });
    }

    var result = await whisperService.TranscribeAudioAsync(targetAudio, config);
    if (result.Success && metadata != null)
    {
        spool.UpdateMetadata(metadata);
    }

    return Results.Ok(result);
});

// API: デバイス一覧取得
app.MapGet("/api/devices", () =>
{
    return Results.Ok(new
    {
        playback = DeviceService.GetPlaybackDevices(),
        capture = DeviceService.GetCaptureDevices()
    });
});

// API: ディスプレイ一覧取得
app.MapGet("/api/screens", () =>
{
    return Results.Ok(new
    {
        screens = ScreenService.GetScreens(),
        virtualDesktop = ScreenService.GetVirtualDesktop()
    });
});

// API: 状態取得
app.MapGet("/api/status", (AudioEngine audioEngine, ScreenCaptureEngine screenEngine) =>
{
    lock (sessionLock)
    {
        return Results.Ok(new
        {
            isRecording = audioEngine.IsRecording,
            sessionId = activeSession?.SessionId,
            currentFile = audioEngine.CurrentOutputFile,
            isCapturing = screenEngine.IsCapturing,
            capturedCount = screenEngine.CapturedCount,
            targetScreen = screenEngine.TargetScreen
        });
    }
});

// API: 録音・画面キャプチャ開始
app.MapPost("/api/record/start", (
    AudioEngine audioEngine, 
    ScreenCaptureEngine screenEngine, 
    SpoolService spool,
    RecordStartRequest? req) =>
{
    lock (sessionLock)
    {
        if (audioEngine.IsRecording || screenEngine.IsCapturing)
        {
            return Results.BadRequest(new { error = "既に記録中です。" });
        }

        int monitorIdx = req?.MonitorIndex ?? 0;
        var screenInfo = ScreenService.ResolveScreen(monitorIdx);

        // スプールディレクトリとメタデータの初期化
        var session = spool.CreateSession(monitorIdx, screenInfo.Name);
        activeSession = new ActiveSessionState(session.SessionId, session.SpoolDirectory);

        var outputPath = Path.Combine(session.SpoolDirectory, "meeting_audio.wav");

        try
        {
            // 1. 音声録音開始
            audioEngine.StartRecording(outputPath, req?.PlaybackDeviceId, req?.CaptureDeviceId);

            // 2. 画面キャプチャ開始 (デフォルト有効)
            bool enableScreen = req?.EnableScreenCapture ?? true;
            if (enableScreen)
            {
                var options = new ScreenCaptureOptions
                {
                    MonitorIndex = monitorIdx
                };
                screenEngine.StartCapture(session.SpoolDirectory, options);
            }

            return Results.Ok(new
            {
                message = "記録を開始しました。",
                sessionId = session.SessionId,
                sessionDir = session.SpoolDirectory,
                audioPath = outputPath,
                screenCaptureEnabled = enableScreen
            });
        }
        catch (Exception ex)
        {
            // ロールバック
            if (audioEngine.IsRecording)
            {
                audioEngine.StopRecordingAsync().GetAwaiter().GetResult();
            }
            if (screenEngine.IsCapturing)
            {
                screenEngine.StopCaptureAsync().GetAwaiter().GetResult();
            }
            activeSession = null;
            return Results.Problem($"記録開始に失敗しました: {ex.Message}");
        }
    }
});

// API: 手動スクショ撮影
app.MapPost("/api/record/screenshot", (ScreenCaptureEngine screenEngine) =>
{
    if (!screenEngine.IsCapturing)
    {
        return Results.BadRequest(new { error = "画面キャプチャ中ではありません。" });
    }

    var evt = screenEngine.CaptureManual();
    return Results.Ok(new
    {
        message = "手動スクショを撮影しました。",
        capture = evt
    });
});

// API: 録音・画面キャプチャ停止 ＆ (任意) GitLab 自動アップロード
// API: 録音・画面キャプチャ停止 ＆ MP3軽量化 ＆ (任意) Whisper文字起こし ＆ (任意) GitLab 自動アップロード
app.MapPost("/api/record/stop", async (
    AudioEngine audioEngine, 
    ScreenCaptureEngine screenEngine,
    SpoolService spool,
    GitLabService gitLabService,
    WhisperService whisperService,
    ConfigService configService) =>
{
    string? stoppingSessionId;
    string? stoppingSessionDir;

    lock (sessionLock)
    {
        if (!audioEngine.IsRecording && !screenEngine.IsCapturing && activeSession == null)
        {
            return Results.BadRequest(new { error = "記録中ではありません。" });
        }

        stoppingSessionId = activeSession?.SessionId;
        stoppingSessionDir = activeSession?.SpoolDirectory;
        activeSession = null;
    }

    try
    {
        if (screenEngine.IsCapturing)
        {
            await screenEngine.StopCaptureAsync();
        }

        if (audioEngine.IsRecording)
        {
            await audioEngine.StopRecordingAsync();
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
                mp3Result = AudioConverter.ConvertWavToMp3(wavPath, mp3Path, targetSampleRate: 24000, targetChannels: 1, desiredBitRate: 64000);
                targetAudioPath = mp3Result.Success ? mp3Path : wavPath;
            }
        }

        // 2. メタデータの確定
        MeetingSessionMetadata? metadata = null;
        if (!string.IsNullOrEmpty(stoppingSessionDir))
        {
            metadata = spool.FinishSession(stoppingSessionDir);
        }

        var appConfig = configService.LoadConfig();

        // 3. Whisper 自動文字起こし (設定有効時)
        WhisperTranscribeResult? whisperResult = null;
        if (appConfig.Whisper.AutoTranscribeOnStop && appConfig.Whisper.IsConfigured && !string.IsNullOrEmpty(targetAudioPath))
        {
            Console.WriteLine($"[Program] 🎙️ 会議終了時 Whisper 自動文字起こし開始: {Path.GetFileName(targetAudioPath)}");
            whisperResult = await whisperService.TranscribeAudioAsync(targetAudioPath, appConfig.Whisper);
            if (whisperResult.Success && metadata != null)
            {
                spool.UpdateMetadata(metadata);
            }
        }

        // 4. GitLab 自動アップロード (設定有効時)
        GitLabUploadResult? gitLabResult = null;
        if (appConfig.GitLab.AutoUploadOnStop && appConfig.GitLab.IsConfigured && !string.IsNullOrEmpty(stoppingSessionDir))
        {
            Console.WriteLine($"[Program] 自動アップロード開始: セッション {stoppingSessionId}");
            gitLabResult = await gitLabService.UploadSessionAsync(
                stoppingSessionDir, 
                appConfig.GitLab, 
                metadata, 
                deleteOnSuccess: true);
        }

        return Results.Ok(new
        {
            message = "記録を停止・保存しました。",
            sessionId = stoppingSessionId,
            audioFile = audioEngine.CurrentOutputFile,
            capturedCount = screenEngine.CapturedCount,
            mp3Conversion = mp3Result,
            whisperTranscription = whisperResult,
            gitLabUpload = gitLabResult
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"停止処理に失敗しました: {ex.Message}");
    }
});

// API: 保管中の未送信スプール一覧取得
app.MapGet("/api/sessions/pending", (SpoolService spool) =>
{
    return Results.Ok(spool.GetPendingSessions());
});

// API: 指定セッションを手動で GitLab へアップロード
app.MapPost("/api/sessions/upload", async (
    SessionUploadRequest req,
    SpoolService spool,
    GitLabService gitLabService,
    ConfigService configService) =>
{
    if (string.IsNullOrWhiteSpace(req.SessionId))
    {
        return Results.BadRequest(new { error = "セッションIDが指定されていません。" });
    }

    var config = configService.LoadConfig().GitLab;
    if (!config.IsConfigured)
    {
        return Results.BadRequest(new { error = "GitLabの設定が未完了です。先に設定を入力・保存してください。" });
    }

    var sessionDir = Path.Combine(spool.SpoolBaseDir, req.SessionId);
    if (!Directory.Exists(sessionDir))
    {
        return Results.NotFound(new { error = $"指定されたセッションが見つかりません: {req.SessionId}" });
    }

    var metadata = spool.GetSessionMetadata(sessionDir);
    var result = await gitLabService.UploadSessionAsync(sessionDir, config, metadata, deleteOnSuccess: true);

    return Results.Ok(result);
});

// API: 保管中の未送信スプールを削除
app.MapDelete("/api/sessions/{sessionId}", (string sessionId, SpoolService spool) =>
{
    bool deleted = spool.DeleteSession(sessionId);
    if (deleted)
    {
        return Results.Ok(new { message = $"セッション {sessionId} を削除しました。" });
    }
    return Results.NotFound(new { error = $"指定されたセッションが見つからないか削除できませんでした: {sessionId}" });
});

// API: アプリケーションの安全なシャットダウン
app.MapPost("/api/app/shutdown", (
    IHostApplicationLifetime lifetime, 
    AudioEngine audioEngine, 
    ScreenCaptureEngine screenEngine) =>
{
    lock (sessionLock)
    {
        if (audioEngine.IsRecording)
        {
            audioEngine.StopRecordingAsync().GetAwaiter().GetResult();
        }
        if (screenEngine.IsCapturing)
        {
            screenEngine.StopCaptureAsync().GetAwaiter().GetResult();
        }
    }

    Task.Run(async () =>
    {
        await Task.Delay(500); // HTTPレスポンス送信完了待ち
        lifetime.StopApplication();
    });

    return Results.Ok(new { message = "アプリケーションを終了します。" });
});

// 全ての非APIリクエストを内蔵UIにフォールバック
app.MapFallback(() => Results.Content(IndexHtml.Content, "text/html; charset=utf-8"));

// サーバー起動完了時のブラウザ自動起動
app.Lifetime.ApplicationStarted.Register(() =>
{
    if (!noBrowser)
    {
        try
        {
            Process.Start(new ProcessStartInfo("http://localhost:5000") { UseShellExecute = true });
            Console.WriteLine("[Program] 🌐 既定ブラウザで http://localhost:5000 を自動起動しました。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Program] ⚠️ ブラウザ自動起動スキップ: {ex.Message}");
        }
    }
});

// LAN IP の取得とバインド情報表示
var hostName = Dns.GetHostName();
var localIps = Dns.GetHostAddresses(hostName)
    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
    .Select(ip => ip.ToString())
    .ToList();

Console.WriteLine("========================================");
Console.WriteLine(" 🎙️ WinMeetingRecorder サーバー起動");
Console.WriteLine("========================================");
Console.WriteLine(" ローカルPCからアクセス:");
Console.WriteLine("   http://localhost:5000");
if (localIps.Count > 0)
{
    Console.WriteLine(" LAN内・外部PC (Mac等) からアクセス:");
    foreach (var ip in localIps)
    {
        Console.WriteLine($"   http://{ip}:5000");
    }
}
Console.WriteLine("========================================");

// 0.0.0.0 にバインド (LAN内からのアクセス許可)
app.Run("http://0.0.0.0:5000");

public record RecordStartRequest(
    string? PlaybackDeviceId, 
    string? CaptureDeviceId, 
    int? MonitorIndex, 
    bool? EnableScreenCapture);

public record SessionUploadRequest(string SessionId);

public record SessionTranscribeRequest(string SessionId);

public record ActiveSessionState(string SessionId, string SpoolDirectory);

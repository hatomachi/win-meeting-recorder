using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NAudio.Wave;
using WinMeetingRecorder.Ai;
using WinMeetingRecorder.Audio;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.GitLab;
using WinMeetingRecorder.Screen;
using WinMeetingRecorder.Spool;
using WinMeetingRecorder.Tray;
using WinMeetingRecorder.Ui;
using WinMeetingRecorder.Whisper;
using WinMeetingRecorder.Workflow;

namespace WinMeetingRecorder;

public static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    [STAThread]
    public static async Task Main(string[] args)
    {
        // ブラウザ自動オープン抑制フラグ判定
        bool noBrowser = args.Any(a => a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase) || a.Equals("-nb", StringComparison.OrdinalIgnoreCase));
        var nonFlagArgs = args.Where(a => !a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase) && !a.Equals("-nb", StringComparison.OrdinalIgnoreCase)).ToArray();

        // CLIモード判定
        if (nonFlagArgs.Length > 0)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);

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
                Console.WriteLine(" 🎵 音声MP3軽量化テスト (Phase 6A)");
                Console.WriteLine("========================================");
                Console.WriteLine($"入力WAV: {inputWav}");
                Console.WriteLine($"出力MP3: {outputMp3}");
                Console.WriteLine("変換仕様: 24000Hz, Mono (1ch), 64kbps");
                Console.WriteLine("----------------------------------------");

                if (!File.Exists(inputWav))
                {
                    Console.WriteLine($"❌ 入力WAVファイルが存在しません: {inputWav}");
                    return;
                }

                var sw = Stopwatch.StartNew();
                var result = AudioConverter.ConvertWavToMp3(inputWav, outputMp3, targetSampleRate: 24000, targetChannels: 1, desiredBitRate: 64000);
                sw.Stop();

                if (result.Success)
                {
                    Console.WriteLine("✅ MP3変換成功！");
                    Console.WriteLine($"   元サイズ:     {result.OriginalSizeBytes / 1024.0:F1} KB");
                    Console.WriteLine($"   変換後サイズ: {result.ConvertedSizeBytes / 1024.0:F1} KB");
                    Console.WriteLine($"   削減率:       {result.ReductionPercentage:F1} %");
                    Console.WriteLine($"   所要時間:     {sw.ElapsedMilliseconds} ms");
                    Console.WriteLine($"   パス:         {outputMp3}");
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
                int durationSec = 10;
                int monitorIdx = 0;
                string outputDir = "test_screen_out";

                if (args.Length > 1 && int.TryParse(args[1], out var p1)) durationSec = p1;
                if (args.Length > 2 && int.TryParse(args[2], out var p2)) monitorIdx = p2;
                if (args.Length > 3) outputDir = args[3];

                outputDir = Path.GetFullPath(outputDir);

                Console.WriteLine("========================================");
                Console.WriteLine(" 🖥️ 画面キャプチャ＆変化検知テスト (Phase 2)");
                Console.WriteLine("========================================");
                Console.WriteLine($"監視時間: {durationSec} 秒");
                Console.WriteLine($"対象モニタ: {monitorIdx}");
                Console.WriteLine($"出力ディレクトリ: {outputDir}");
                Console.WriteLine("スライドをめくったり、ウィンドウを動かしてみてください...");
                Console.WriteLine("----------------------------------------");

                using var engine = new ScreenCaptureEngine();
                try
                {
                    engine.OnCaptured += (evt) =>
                    {
                        Console.WriteLine($"📸 撮影検知! [{evt.CapturedAt:HH:mm:ss}] 原因: {evt.Reason} (差分: {evt.DiffScore * 100:F1}%) -> {Path.GetFileName(evt.FilePath)}");
                    };

                    var options = new ScreenCaptureOptions
                    {
                        MonitorIndex = monitorIdx,
                        MinIntervalSeconds = 2.0,
                        KeyframeIntervalSeconds = 30.0,
                        DiffThreshold = 0.03f,
                        JpegQuality = 80L
                    };

                    engine.StartCapture(outputDir, options);
                    Console.WriteLine("🟢 画面監視中...");

                    for (int i = durationSec; i > 0; i--)
                    {
                        Console.Write($"\r残り時間: {i} 秒 (撮影済み: {engine.CapturedCount} 枚)   ");
                        Thread.Sleep(1000);
                    }
                    Console.WriteLine();

                    Console.WriteLine("⏹️ 監視停止中...");
                    engine.StopCaptureAsync().GetAwaiter().GetResult();

                    Console.WriteLine("========================================");
                    Console.WriteLine("✅ 画面キャプチャテスト完了！");
                    Console.WriteLine($"   撮影枚数: {engine.CapturedCount} 枚");
                    Console.WriteLine($"   保存先ディレクトリ: {Path.Combine(outputDir, "images")}");
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

            if (command == "--test-ai")
            {
                var configService = new ConfigService();
                var promptService = new PromptService();
                var aiService = new AiService(promptService);
                var config = configService.LoadConfig().Ai;
                if (nonFlagArgs.Length > 1) config.Engine = nonFlagArgs[1];

                Console.WriteLine("========================================");
                Console.WriteLine(" 🤖 AI CLI 接続テスト (Phase 7A)");
                Console.WriteLine("========================================");
                Console.WriteLine($"Engine:     {config.Engine}");
                Console.WriteLine($"Model:      {(string.IsNullOrEmpty(config.Model) ? "(既定)" : config.Model)}");
                Console.WriteLine($"PromptFile: {promptService.ActivePromptFilePath}");
                Console.WriteLine("----------------------------------------");

                var status = aiService.CheckStatusAsync(config).GetAwaiter().GetResult();
                if (status.Available)
                {
                    Console.WriteLine($"✅ {config.Engine} CLI 利用可能！");
                    Console.WriteLine($"   パス:       {status.CommandPath}");
                    Console.WriteLine($"   バージョン: {status.Version}");
                }
                else
                {
                    Console.WriteLine($"❌ {config.Engine} CLI 利用不可: {status.Error}");
                }
                Console.WriteLine("========================================");
                return;
            }

            if (command == "--help" || command == "-h")
            {
                Console.WriteLine("========================================");
                Console.WriteLine(" 🎙️ WinMeetingRecorder ヘルプ");
                Console.WriteLine("========================================");
                Console.WriteLine("引数なしで実行すると、タスクトレイに常駐し Web UI (http://0.0.0.0:5000) が起動します。\n");
                Console.WriteLine("CLIコマンド一覧:");
                Console.WriteLine("  --list-devices, -l             : オーディオ入出力デバイス一覧を表示");
                Console.WriteLine("  --list-screens, -ls            : ディスプレイ一覧を表示");
                Console.WriteLine("  --test-audio, -t [秒数] [出力パス] : 音声合成（相手の声＋マイク）テスト録音");
                Console.WriteLine("  --test-mp3 [WAVパス] [MP3パス] : 音声MP3軽量化 (24kHz Mono 64kbps) テスト");
                Console.WriteLine("  --test-screen, -ts [秒数] [モニタIndex] [出力先] : 画面変化検知スクショテスト");
                Console.WriteLine("  --test-gitlab [URL] [Project] [PAT] : GitLab REST API 接続テスト");
                Console.WriteLine("  --test-whisper [BaseUrl] [Audio] [Key] : Whisper API 文字起こしテスト");
                Console.WriteLine("  --test-ai [Engine(copilot/claude)] : AI CLI 接続テスト");
                Console.WriteLine("  --pending-spool                : ローカルスプールに残っている未送信一覧");
                Console.WriteLine("  --no-browser, -nb              : 起動時に既定ブラウザを自動で開かない");
                Console.WriteLine("  --help, -h                     : このヘルプを表示");
                Console.WriteLine("========================================");
                return;
            }
        }

        // ==========================================
        // 通常起動: ASP.NET Core + タスクトレイ常駐
        // ==========================================
        var builder = WebApplication.CreateBuilder(args);

        // DI コンテナ登録
        builder.Services.AddSingleton<ConfigService>();
        builder.Services.AddSingleton<PromptService>();
        builder.Services.AddSingleton<AiService>();
        builder.Services.AddSingleton<SpoolService>();
        builder.Services.AddSingleton<GitLabService>();
        builder.Services.AddSingleton<WhisperService>();
        builder.Services.AddSingleton<AudioEngine>();
        builder.Services.AddSingleton<ScreenCaptureEngine>();
        builder.Services.AddSingleton<RecordingWorkflowService>();

        var app = builder.Build();

        // 静的ファイル配信 (フォールバック用)
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Web UI: 単一exe内蔵HTML配信
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
            var mp3Path = Path.Combine(sessionDir, "meeting_audio.mp3");
            var wavPath = Path.Combine(sessionDir, "meeting_audio.wav");

            string targetAudio;
            if (File.Exists(mp3Path))
            {
                targetAudio = mp3Path;
            }
            else if (File.Exists(wavPath))
            {
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

        // API: AI CLI 接続テスト
        app.MapPost("/api/ai/test", async (AiService aiService, AiConfig config) =>
        {
            var result = await aiService.CheckStatusAsync(config);
            return Results.Ok(result);
        });

        // API: 議事録プロンプト取得
        app.MapGet("/api/ai/prompt", (PromptService promptService) =>
        {
            return Results.Ok(new
            {
                prompt = promptService.LoadPrompt(),
                filePath = promptService.ActivePromptFilePath
            });
        });

        // API: 議事録プロンプト保存
        app.MapPost("/api/ai/prompt", (PromptService promptService, PromptSaveRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Prompt))
            {
                return Results.BadRequest(new { error = "プロンプト内容が空です。" });
            }
            promptService.SavePrompt(req.Prompt);
            return Results.Ok(new { message = "プロンプトを保存しました。", filePath = promptService.ActivePromptFilePath });
        });

        // API: 指定セッションの議事録手動生成
        app.MapPost("/api/ai/minutes", async (
            SessionMinutesRequest req,
            SpoolService spool,
            AiService aiService,
            ConfigService configService) =>
        {
            if (string.IsNullOrWhiteSpace(req.SessionId))
            {
                return Results.BadRequest(new { error = "セッションIDが指定されていません。" });
            }

            var sessionDir = Path.Combine(spool.SpoolBaseDir, req.SessionId);
            if (!Directory.Exists(sessionDir))
            {
                return Results.NotFound(new { error = $"指定されたセッションが見つかりません: {req.SessionId}" });
            }

            var metadata = spool.GetSessionMetadata(sessionDir);
            if (metadata == null)
            {
                return Results.NotFound(new { error = "メタデータが見つかりません。" });
            }

            var config = configService.LoadConfig().Ai;
            var result = await aiService.GenerateMinutesAsync(metadata, config);
            if (result.Success)
            {
                spool.UpdateMetadata(metadata);
            }

            return Results.Ok(result);
        });

        // API: 議事録チャット修正依頼 (--resume)
        app.MapPost("/api/ai/chat", async (
            AiChatRequest req,
            SpoolService spool,
            AiService aiService,
            ConfigService configService) =>
        {
            if (string.IsNullOrWhiteSpace(req.SessionId))
            {
                return Results.BadRequest(new { error = "セッションIDが指定されていません。" });
            }
            if (string.IsNullOrWhiteSpace(req.Message))
            {
                return Results.BadRequest(new { error = "修正依頼メッセージが空です。" });
            }

            var sessionDir = Path.Combine(spool.SpoolBaseDir, req.SessionId);
            if (!Directory.Exists(sessionDir))
            {
                return Results.NotFound(new { error = $"指定されたセッションが見つかりません: {req.SessionId}" });
            }

            var metadata = spool.GetSessionMetadata(sessionDir);
            if (metadata == null)
            {
                return Results.NotFound(new { error = "メタデータが見つかりません。" });
            }

            var config = configService.LoadConfig().Ai;
            var result = await aiService.ChatReviseAsync(metadata, req.Message, config);
            if (result.Success)
            {
                spool.UpdateMetadata(metadata);
            }

            return Results.Ok(result);
        });

        // API: 議事録 Markdown テキスト取得
        app.MapGet("/api/spool/{sessionId}/minutes", (string sessionId, SpoolService spool) =>
        {
            var sessionDir = Path.Combine(spool.SpoolBaseDir, sessionId);
            var minutesPath = Path.Combine(sessionDir, "MINUTES.md");
            if (!File.Exists(minutesPath))
            {
                return Results.NotFound(new { error = "議事録が存在しません。" });
            }

            var content = File.ReadAllText(minutesPath);
            return Results.Content(content, "text/plain; charset=utf-8");
        });

        // API: パイプラインステップ単独実行/再開
        app.MapPost("/api/pipeline/run-step", async (
            PipelineStepRequest req,
            SpoolService spool,
            WhisperService whisperService,
            AiService aiService,
            GitLabService gitLabService,
            ConfigService configService) =>
        {
            if (string.IsNullOrWhiteSpace(req.SessionId))
            {
                return Results.BadRequest(new { error = "セッションIDが指定されていません。" });
            }

            var sessionDir = Path.Combine(spool.SpoolBaseDir, req.SessionId);
            if (!Directory.Exists(sessionDir))
            {
                return Results.NotFound(new { error = $"指定されたセッションが見つかりません: {req.SessionId}" });
            }

            var metadata = spool.GetSessionMetadata(sessionDir);
            if (metadata == null)
            {
                return Results.NotFound(new { error = "メタデータが見つかりません。" });
            }

            var appConfig = configService.LoadConfig();

            if (req.Step == "transcribe")
            {
                metadata.Pipeline.Transcribe = "running";
                spool.UpdateMetadata(metadata);

                var mp3Path = Path.Combine(sessionDir, "meeting_audio.mp3");
                var wavPath = Path.Combine(sessionDir, "meeting_audio.wav");
                string targetAudio = File.Exists(mp3Path) ? mp3Path : wavPath;

                var trResult = await whisperService.TranscribeAudioAsync(targetAudio, appConfig.Whisper);
                if (trResult.Success)
                {
                    metadata.Pipeline.Transcribe = "success";
                    spool.UpdateMetadata(metadata);

                    if (req.AutoFollow && appConfig.Ai.AutoGenerateMinutes)
                    {
                        metadata.Pipeline.Minutes = "running";
                        spool.UpdateMetadata(metadata);
                        var aiResult = await aiService.GenerateMinutesAsync(metadata, appConfig.Ai, trResult.Response);
                        if (aiResult.Success)
                        {
                            metadata.Pipeline.Minutes = "success";
                            spool.UpdateMetadata(metadata);

                            if (appConfig.GitLab.AutoUploadOnStop && appConfig.GitLab.IsConfigured)
                            {
                                metadata.Pipeline.Upload = "running";
                                spool.UpdateMetadata(metadata);
                                var upResult = await gitLabService.UploadSessionAsync(sessionDir, appConfig.GitLab, metadata, true);
                                if (!upResult.Success)
                                {
                                    metadata.Pipeline.Upload = "error";
                                    metadata.Pipeline.ErrorMessage = upResult.Message;
                                    spool.UpdateMetadata(metadata);
                                }
                            }
                        }
                        else
                        {
                            metadata.Pipeline.Minutes = "error";
                            metadata.Pipeline.ErrorMessage = aiResult.Message;
                            spool.UpdateMetadata(metadata);
                        }
                    }

                    return Results.Ok(new { success = true, step = "transcribe", message = "文字起こしが完了しました。" });
                }
                else
                {
                    metadata.Pipeline.Transcribe = "error";
                    metadata.Pipeline.ErrorMessage = trResult.Message;
                    spool.UpdateMetadata(metadata);
                    return Results.Ok(new { success = false, step = "transcribe", message = trResult.Message });
                }
            }
            else if (req.Step == "minutes")
            {
                metadata.Pipeline.Minutes = "running";
                spool.UpdateMetadata(metadata);

                var aiResult = await aiService.GenerateMinutesAsync(metadata, appConfig.Ai);
                if (aiResult.Success)
                {
                    metadata.Pipeline.Minutes = "success";
                    spool.UpdateMetadata(metadata);

                    if (req.AutoFollow && appConfig.GitLab.AutoUploadOnStop && appConfig.GitLab.IsConfigured)
                    {
                        metadata.Pipeline.Upload = "running";
                        spool.UpdateMetadata(metadata);
                        var upResult = await gitLabService.UploadSessionAsync(sessionDir, appConfig.GitLab, metadata, true);
                        if (!upResult.Success)
                        {
                            metadata.Pipeline.Upload = "error";
                            metadata.Pipeline.ErrorMessage = upResult.Message;
                            spool.UpdateMetadata(metadata);
                        }
                    }

                    return Results.Ok(new { success = true, step = "minutes", message = "議事録を生成しました。", minutes = aiResult.MinutesMarkdown });
                }
                else
                {
                    metadata.Pipeline.Minutes = "error";
                    metadata.Pipeline.ErrorMessage = aiResult.Message;
                    spool.UpdateMetadata(metadata);
                    return Results.Ok(new { success = false, step = "minutes", message = aiResult.Message });
                }
            }
            else if (req.Step == "upload")
            {
                metadata.Pipeline.Upload = "running";
                spool.UpdateMetadata(metadata);

                var upResult = await gitLabService.UploadSessionAsync(sessionDir, appConfig.GitLab, metadata, true);
                if (upResult.Success)
                {
                    return Results.Ok(new { success = true, step = "upload", message = "GitLabへアップロードしました。" });
                }
                else
                {
                    metadata.Pipeline.Upload = "error";
                    metadata.Pipeline.ErrorMessage = upResult.Message;
                    spool.UpdateMetadata(metadata);
                    return Results.Ok(new { success = false, step = "upload", message = upResult.Message });
                }
            }

            return Results.BadRequest(new { error = $"未知のステップです: {req.Step}" });
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

        // API: 状態取得 (ワークフロー統合)
        app.MapGet("/api/status", (
            RecordingWorkflowService workflow, 
            AudioEngine audioEngine, 
            ScreenCaptureEngine screenEngine) =>
        {
            return Results.Ok(new
            {
                isRecording = workflow.IsRecording,
                sessionId = workflow.ActiveSession?.SessionId,
                currentFile = audioEngine.CurrentOutputFile,
                isCapturing = screenEngine.IsCapturing,
                capturedCount = screenEngine.CapturedCount,
                targetScreen = screenEngine.TargetScreen,
                audioLevel = audioEngine.CurrentAudioLevel
            });
        });

        // API: 録音・画面キャプチャ開始 (ワークフロー統合)
        app.MapPost("/api/record/start", async (
            RecordingWorkflowService workflow, 
            RecordStartRequest? req) =>
        {
            var opt = new RecordStartOptions(
                req?.PlaybackDeviceId,
                req?.CaptureDeviceId,
                req?.MonitorIndex,
                req?.EnableScreenCapture);

            var res = await workflow.StartRecordingAsync(opt);
            if (!res.Success)
            {
                return Results.BadRequest(new { error = res.Message });
            }

            return Results.Ok(new
            {
                message = res.Message,
                sessionId = res.SessionId,
                sessionDir = res.SessionDir,
                audioPath = res.AudioPath,
                screenCaptureEnabled = res.ScreenCaptureEnabled
            });
        });

        // API: 手動スクショ撮影 (ワークフロー統合)
        app.MapPost("/api/record/screenshot", (RecordingWorkflowService workflow) =>
        {
            var evt = workflow.CaptureManual();
            if (evt == null)
            {
                return Results.BadRequest(new { error = "画面キャプチャ中ではありません。" });
            }

            return Results.Ok(new
            {
                message = "手動スクショを撮影しました。",
                capture = evt
            });
        });

        // API: 録音停止 ＆ パイプライン自動化 (ワークフロー統合)
        app.MapPost("/api/record/stop", async (RecordingWorkflowService workflow) =>
        {
            var res = await workflow.StopRecordingAsync();
            if (!res.Success)
            {
                return Results.BadRequest(new { error = res.Message });
            }

            return Results.Ok(new
            {
                message = res.Message,
                sessionId = res.SessionId,
                audioFile = res.AudioFile,
                capturedCount = res.CapturedCount,
                mp3Conversion = res.Mp3Conversion,
                whisperTranscription = res.WhisperTranscription,
                aiMinutes = res.AiMinutes,
                gitLabUpload = res.GitLabUpload,
                pipeline = res.Pipeline
            });
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

        // API: アプリケーションの安全なシャットダウン (トレイ連携)
        app.MapPost("/api/app/shutdown", async (RecordingWorkflowService workflow) =>
        {
            if (workflow.IsRecording)
            {
                await workflow.StopRecordingAsync();
            }

            Task.Run(async () =>
            {
                await Task.Delay(300);
                Application.Exit();
            });

            return Results.Ok(new { message = "アプリケーションを終了します。" });
        });

        // 全ての非APIリクエストを内蔵UIにフォールバック
        app.MapFallback(() => Results.Content(IndexHtml.Content, "text/html; charset=utf-8"));

        // LAN IP の取得とバインド情報表示
        var hostName = Dns.GetHostName();
        var localIps = Dns.GetHostAddresses(hostName)
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            .Select(ip => ip.ToString())
            .ToList();

        Console.WriteLine("========================================");
        Console.WriteLine(" 🎙️ WinMeetingRecorder タスクトレイ常駐起動");
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

        // Kestrel サーバーを非同期で開始
        var serverTask = app.RunAsync("http://0.0.0.0:5000");

        // Windows Forms 初期化
        ApplicationConfiguration.Initialize();

        var workflowService = app.Services.GetRequiredService<RecordingWorkflowService>();
        var audioEngine = app.Services.GetRequiredService<AudioEngine>();

        using var trayService = new TrayIconService(workflowService, audioEngine, () =>
        {
            Task.Run(async () =>
            {
                if (workflowService.IsRecording)
                {
                    await workflowService.StopRecordingAsync();
                }
                await app.StopAsync();
                Application.Exit();
            });
        });

        // 起動時に既定ブラウザを自動オープン
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

        // STA メッセージループの開始 (タスクトレイ常駐)
        Application.Run();

        // 終了待ち
        try
        {
            await serverTask;
        }
        catch { /* ignore */ }
    }
}

public record RecordStartRequest(
    string? PlaybackDeviceId, 
    string? CaptureDeviceId, 
    int? MonitorIndex, 
    bool? EnableScreenCapture);

public record SessionUploadRequest(string SessionId);

public record SessionTranscribeRequest(string SessionId);

public record SessionMinutesRequest(string SessionId);

public record AiChatRequest(string SessionId, string Message);

public record PipelineStepRequest(string SessionId, string Step, bool AutoFollow = true);

public record PromptSaveRequest(string Prompt);

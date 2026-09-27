using NAudio.Wave;
using WinMeetingRecorder.Audio;
using WinMeetingRecorder.Screen;
using WinMeetingRecorder.Ui;

var builder = WebApplication.CreateBuilder(args);

// CLIモード判定
if (args.Length > 0)
{
    var command = args[0].ToLowerInvariant();

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

    if (command == "--help" || command == "-h")
    {
        Console.WriteLine("========================================");
        Console.WriteLine(" 🎙️ WinMeetingRecorder ヘルプ");
        Console.WriteLine("========================================");
        Console.WriteLine("引数なしで実行すると、Web UI (http://localhost:5000) が起動します。\n");
        Console.WriteLine("CLIコマンド一覧:");
        Console.WriteLine("  --list-devices, -l             : オーディオ入出力デバイス一覧を表示");
        Console.WriteLine("  --list-screens, -ls            : ディスプレイ一覧を表示");
        Console.WriteLine("  --test-audio, -t [秒数] [出力パス] : 音声合成（相手の声＋マイク）テスト録音");
        Console.WriteLine("  --test-screen, -ts [秒数] [モニタIndex] [出力先] : 画面変化検知スクショテスト");
        Console.WriteLine("  --help, -h                     : このヘルプを表示");
        Console.WriteLine("========================================");
        return;
    }
}

// ==========================================
// ASP.NET Core Minimal API サーバー (Web UI)
// ==========================================
builder.Services.AddSingleton<AudioEngine>();
builder.Services.AddSingleton<ScreenCaptureEngine>();

var app = builder.Build();

// 静的ファイル配信 (フォールバック用)
app.UseDefaultFiles();
app.UseStaticFiles();

// Web UI: 単一exe内蔵HTML配信 (404防止・単一exeポータブル対応)
app.MapGet("/", () => Results.Content(IndexHtml.Content, "text/html; charset=utf-8"));
app.MapGet("/index.html", () => Results.Content(IndexHtml.Content, "text/html; charset=utf-8"));

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
    return Results.Ok(new
    {
        isRecording = audioEngine.IsRecording,
        currentFile = audioEngine.CurrentOutputFile,
        isCapturing = screenEngine.IsCapturing,
        capturedCount = screenEngine.CapturedCount,
        targetScreen = screenEngine.TargetScreen
    });
});

// API: 録音・画面キャプチャ開始
app.MapPost("/api/record/start", (AudioEngine audioEngine, ScreenCaptureEngine screenEngine, RecordStartRequest? req) =>
{
    if (audioEngine.IsRecording || screenEngine.IsCapturing)
    {
        return Results.BadRequest(new { error = "既に記録中です。" });
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var spoolDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinMeetingRecorder", "spool", timestamp);
    var outputPath = Path.Combine(spoolDir, "meeting_audio.wav");

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
                MonitorIndex = req?.MonitorIndex ?? 0
            };
            screenEngine.StartCapture(spoolDir, options);
        }

        return Results.Ok(new
        {
            message = "記録を開始しました。",
            sessionDir = spoolDir,
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
        return Results.Problem($"記録開始に失敗しました: {ex.Message}");
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

// API: 録音・画面キャプチャ停止
app.MapPost("/api/record/stop", async (AudioEngine audioEngine, ScreenCaptureEngine screenEngine) =>
{
    if (!audioEngine.IsRecording && !screenEngine.IsCapturing)
    {
        return Results.BadRequest(new { error = "記録中ではありません。" });
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

        return Results.Ok(new
        {
            message = "記録を停止・保存しました。",
            audioFile = audioEngine.CurrentOutputFile,
            capturedCount = screenEngine.CapturedCount
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"停止処理に失敗しました: {ex.Message}");
    }
});

// 全ての非APIリクエストを内蔵UIにフォールバック
app.MapFallback(() => Results.Content(IndexHtml.Content, "text/html; charset=utf-8"));

Console.WriteLine("========================================");
Console.WriteLine(" 🎙️ WinMeetingRecorder サーバー起動");
Console.WriteLine(" ブラウザで以下のURLを開いてください:");
Console.WriteLine("   http://localhost:5000");
Console.WriteLine("========================================");

app.Run("http://localhost:5000");

public record RecordStartRequest(
    string? PlaybackDeviceId, 
    string? CaptureDeviceId, 
    int? MonitorIndex, 
    bool? EnableScreenCapture);

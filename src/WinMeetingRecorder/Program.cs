using NAudio.Wave;
using WinMeetingRecorder.Audio;
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
}

// ==========================================
// ASP.NET Core Minimal API サーバー (Web UI)
// ==========================================
builder.Services.AddSingleton<AudioEngine>();

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

// API: 状態取得
app.MapGet("/api/status", (AudioEngine engine) =>
{
    return Results.Ok(new
    {
        isRecording = engine.IsRecording,
        currentFile = engine.CurrentOutputFile
    });
});

// API: 録音開始
app.MapPost("/api/record/start", (AudioEngine engine, RecordStartRequest? req) =>
{
    if (engine.IsRecording)
    {
        return Results.BadRequest(new { error = "既に録音中です。" });
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var spoolDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinMeetingRecorder", "spool", timestamp);
    var outputPath = Path.Combine(spoolDir, "meeting_audio.wav");

    try
    {
        engine.StartRecording(outputPath, req?.PlaybackDeviceId, req?.CaptureDeviceId);
        return Results.Ok(new
        {
            message = "録音を開始しました。",
            sessionDir = spoolDir,
            audioPath = outputPath
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"録音開始に失敗しました: {ex.Message}");
    }
});

// API: 録音停止
app.MapPost("/api/record/stop", async (AudioEngine engine) =>
{
    if (!engine.IsRecording)
    {
        return Results.BadRequest(new { error = "録音中ではありません。" });
    }

    try
    {
        await engine.StopRecordingAsync();
        return Results.Ok(new
        {
            message = "録音を停止・保存しました。",
            file = engine.CurrentOutputFile
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"録音停止に失敗しました: {ex.Message}");
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

public record RecordStartRequest(string? PlaybackDeviceId, string? CaptureDeviceId);

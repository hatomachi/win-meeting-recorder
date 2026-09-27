using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace WinMeetingRecorder.Screen;

/// <summary>
/// 画面キャプチャ設定オプション
/// </summary>
public record ScreenCaptureOptions
{
    /// <summary>
    /// 対象モニター (0: プライマリ, 1..N: 個別画面, -1: 仮想デスクトップ全体)
    /// </summary>
    public int MonitorIndex { get; init; } = 0;

    /// <summary>
    /// 画面変化検知閾値 (0.05 = 5%以上の変化でスライド送り等を検知)
    /// </summary>
    public double DiffThreshold { get; init; } = 0.05;

    /// <summary>
    /// 変化検知後の最小クールダウン秒数 (過剰連写防止。デフォルト2秒)
    /// </summary>
    public int MinIntervalSec { get; init; } = 2;

    /// <summary>
    /// 画面変化がない場合の定期キーフレーム保存間隔秒数 (0で無効、デフォルト60秒)
    /// </summary>
    public int MaxIntervalSec { get; init; } = 60;

    /// <summary>
    /// 監視ループの周期ミリ秒 (デフォルト1000ms = 1秒)
    /// </summary>
    public int CheckIntervalMs { get; init; } = 1000;

    /// <summary>
    /// スライド変化検知後の安定待ちデバウンス時間ミリ秒 (切り替えアニメーション完了待ち。デフォルト500ms)
    /// </summary>
    public int DebounceStabilityMs { get; init; } = 500;

    /// <summary>
    /// フル解像度JPEGの保存品質 (1〜100、デフォルト80)
    /// </summary>
    public long JpegQuality { get; init; } = 80L;
}

/// <summary>
/// 画面撮影イベント情報
/// </summary>
public record ScreenCaptureEvent(
    string FilePath,
    string FileName,
    string RelativePath,
    double Diff,
    string Reason,
    int ElapsedSeconds,
    DateTime Timestamp);

/// <summary>
/// GDI+ CopyFromScreen による画面キャプチャ＆変化検知エンジン
/// </summary>
[SupportedOSPlatform("windows")]
public class ScreenCaptureEngine : IDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _disposed;

    private ScreenInfo? _targetScreen;
    private byte[]? _lastSavedThumb;
    private DateTime _startTime;
    private DateTime _lastCaptureTime;
    private int _capturedCount;
    private string? _imagesDirectory;

    public bool IsCapturing { get; private set; }
    public int CapturedCount => _capturedCount;
    public string? CurrentOutputDir => _imagesDirectory;
    public ScreenInfo? TargetScreen => _targetScreen;

    /// <summary>
    /// 画面が撮影・保存された際に発火するイベント
    /// </summary>
    public event Action<ScreenCaptureEvent>? OnCaptured;

    /// <summary>
    /// 定期チェック時に差分率が計算された際に発火する診断イベント (オプション)
    /// </summary>
    public event Action<double, int>? OnDiffCalculated;

    /// <summary>
    /// 画面キャプチャおよび自動変化検知監視を開始します
    /// </summary>
    /// <param name="outputDir">保存先ディレクトリ (この配下に images サブフォルダが作られます)</param>
    /// <param name="options">キャプチャ設定オプション</param>
    public void StartCapture(string outputDir, ScreenCaptureOptions? options = null)
    {
        lock (_lock)
        {
            if (IsCapturing)
            {
                throw new InvalidOperationException("画面キャプチャは既に実行中です。");
            }

            options ??= new ScreenCaptureOptions();
            _targetScreen = ScreenService.ResolveScreen(options.MonitorIndex);

            // outputDir/images ディレクトリを準備
            _imagesDirectory = Path.Combine(outputDir, "images");
            Directory.CreateDirectory(_imagesDirectory);

            _cts = new CancellationTokenSource();
            _startTime = DateTime.UtcNow;
            _lastCaptureTime = DateTime.MinValue;
            _capturedCount = 0;
            _lastSavedThumb = null;
            IsCapturing = true;

            var ct = _cts.Token;

            // 1. 初回画面の即座撮影
            try
            {
                CaptureAndSaveInternal(_targetScreen.Bounds, "initial", 1.0, options.JpegQuality);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScreenCapture] 初回キャプチャ警告: {ex.Message}");
            }

            // 2. バックグラウンド変化検知ループ開始
            _captureTask = Task.Run(async () => await RunMonitoringLoopAsync(options, ct), ct);
        }
    }

    /// <summary>
    /// 画面キャプチャを停止し、完了を待機します
    /// </summary>
    public async Task StopCaptureAsync()
    {
        CancellationTokenSource? cts;
        Task? task;

        lock (_lock)
        {
            if (!IsCapturing) return;
            IsCapturing = false;
            cts = _cts;
            task = _captureTask;
        }

        if (cts != null)
        {
            cts.Cancel();
            if (task != null)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException) { /* 正常終了 */ }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScreenCapture] 監視ループ終了エラー: {ex.Message}");
                }
            }
            cts.Dispose();
        }

        lock (_lock)
        {
            _cts = null;
            _captureTask = null;
        }
    }

    /// <summary>
    /// 手動でスクショを即時撮影します
    /// </summary>
    public ScreenCaptureEvent? CaptureManual(long jpegQuality = 80L)
    {
        lock (_lock)
        {
            if (!IsCapturing || _targetScreen == null || string.IsNullOrEmpty(_imagesDirectory))
            {
                return null;
            }

            return CaptureAndSaveInternal(_targetScreen.Bounds, "manual", 1.0, jpegQuality);
        }
    }

    /// <summary>
    /// 変化検知＆デバウンス監視ループ
    /// </summary>
    private async Task RunMonitoringLoopAsync(ScreenCaptureOptions options, CancellationToken ct)
    {
        var bounds = _targetScreen!.Bounds;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.CheckIntervalMs, ct);

                // 1. 現在の画面をキャプチャしサムネイル生成
                byte[] currentThumb;
                using (var currentBmp = CaptureScreenBitmap(bounds))
                {
                    currentThumb = ScreenDiffDetector.CreateGrayscaleThumbnail(currentBmp);
                }

                // 2. 前回収得サムネイルとの差分計算
                double diff = ScreenDiffDetector.ComputeDifference(_lastSavedThumb, currentThumb);
                int elapsedSec = (int)(DateTime.UtcNow - _startTime).TotalSeconds;
                int timeSinceLastCapture = (int)(DateTime.UtcNow - _lastCaptureTime).TotalSeconds;

                OnDiffCalculated?.Invoke(diff, elapsedSec);

                // 3. 判定ロジック
                bool shouldCapture = false;
                string reason = "";

                if (diff >= options.DiffThreshold && timeSinceLastCapture >= options.MinIntervalSec)
                {
                    // スライド変化等を検知
                    shouldCapture = true;
                    reason = "diff";

                    // デバウンス待機: アニメーション・ページめくり効果完了まで待つ
                    if (options.DebounceStabilityMs > 0)
                    {
                        await Task.Delay(options.DebounceStabilityMs, ct);
                    }
                }
                else if (options.MaxIntervalSec > 0 && timeSinceLastCapture >= options.MaxIntervalSec)
                {
                    // 定期キーフレーム
                    shouldCapture = true;
                    reason = "timer";
                }

                // 4. 撮影＆フル解像度保存
                if (shouldCapture && !ct.IsCancellationRequested)
                {
                    lock (_lock)
                    {
                        if (IsCapturing)
                        {
                            CaptureAndSaveInternal(bounds, reason, diff, options.JpegQuality);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScreenCapture] 監視中エラー: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 画面をキャプチャし、JPEG保存してイベントを発火します (内部同期用)
    /// </summary>
    private ScreenCaptureEvent CaptureAndSaveInternal(
        Rectangle bounds, 
        string reason, 
        double diff, 
        long jpegQuality)
    {
        using var bmp = CaptureScreenBitmap(bounds);

        // 次回比較用サムネイルを更新
        _lastSavedThumb = ScreenDiffDetector.CreateGrayscaleThumbnail(bmp);

        var now = DateTime.Now;
        var elapsedSec = (int)(DateTime.UtcNow - _startTime).TotalSeconds;
        var filename = $"screen_{now:yyyyMMdd_HHmmss}.jpg";
        var destPath = Path.Combine(_imagesDirectory!, filename);

        // 同一秒に複数撮影された場合の重複回避
        int dupCount = 1;
        while (File.Exists(destPath))
        {
            filename = $"screen_{now:yyyyMMdd_HHmmss}_{dupCount++}.jpg";
            destPath = Path.Combine(_imagesDirectory!, filename);
        }

        SaveBitmapAsJpeg(bmp, destPath, jpegQuality);

        _lastCaptureTime = DateTime.UtcNow;
        _capturedCount++;

        var captureEvent = new ScreenCaptureEvent(
            destPath,
            filename,
            Path.Combine("images", filename),
            diff,
            reason,
            elapsedSec,
            now);

        OnCaptured?.Invoke(captureEvent);
        return captureEvent;
    }

    /// <summary>
    /// GDI+ CopyFromScreen による画面Bitmapのキャプチャ
    /// </summary>
    public static Bitmap CaptureScreenBitmap(Rectangle bounds)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>
    /// Bitmap を品質指定 JPEG として保存
    /// </summary>
    public static void SaveBitmapAsJpeg(Bitmap bmp, string filePath, long quality = 80L)
    {
        var encoder = GetEncoder(ImageFormat.Jpeg);
        if (encoder == null)
        {
            bmp.Save(filePath, ImageFormat.Jpeg);
            return;
        }

        using var encoderParameters = new EncoderParameters(1);
        using var qualityParam = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1L, 100L));
        encoderParameters.Param[0] = qualityParam;
        bmp.Save(filePath, encoder, encoderParameters);
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
        foreach (ImageCodecInfo codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsCapturing)
        {
            StopCaptureAsync().GetAwaiter().GetResult();
        }
    }
}

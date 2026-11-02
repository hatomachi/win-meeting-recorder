using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
    /// 監視ループの周期ミリ秒 (デフォルト 5000ms = 5秒)
    /// </summary>
    public int CheckIntervalMs { get; init; } = 5000;

    /// <summary>
    /// 画面変化がない場合の定期キーフレーム保存間隔秒数 (0で無効化。デフォルト 0)
    /// </summary>
    public int MaxIntervalSec { get; init; } = 0;

    /// <summary>
    /// 差分検知アルゴリズム ("phash" = pHash 8x8 DCT, "hybrid" = 従来輝度差ハイブリッド)
    /// </summary>
    public string DiffAlgorithm { get; init; } = "phash";

    /// <summary>
    /// pHash ハミング距離閾値 (ハミング距離 > PhashThreshold で変化検知。デフォルト 5)
    /// </summary>
    public int PhashThreshold { get; init; } = 5;

    /// <summary>
    /// 従来hybrid方式の画面変化検知閾値 (0.05 = 5%以上の変化でスライド送り等を検知)
    /// </summary>
    public double DiffThreshold { get; init; } = 0.05;

    /// <summary>
    /// 変化検知後の最小クールダウン秒数 (過剰連写防止。デフォルト2秒)
    /// </summary>
    public int MinIntervalSec { get; init; } = 2;

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
/// (画面ロック自動復帰・外部モニター切断時のプライマリ自動フォールバック対応)
/// </summary>
[SupportedOSPlatform("windows")]
public class ScreenCaptureEngine : IDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _disposed;

    private int _requestedMonitorIndex;
    private ScreenInfo? _targetScreen;
    private byte[]? _lastSavedThumb;
    private ulong? _lastSavedPhash;
    private DateTime _startTime;
    private DateTime _lastCaptureTime;
    private int _capturedCount;
    private string? _imagesDirectory;

    public bool IsCapturing { get; private set; }
    public int CapturedCount => _capturedCount;
    public string? CurrentOutputDir => _imagesDirectory;
    public ScreenInfo? TargetScreen => _targetScreen;

    // 健全性・監視用テレメトリ
    public DateTime LastCheckUtc { get; private set; } = DateTime.MinValue;
    public string HealthStatus { get; private set; } = "idle";
    public string? LastWarningMessage { get; private set; }
    public int FallbackCount { get; private set; }

    /// <summary>
    /// 画面が撮影・保存された際に発火するイベント
    /// </summary>
    public event Action<ScreenCaptureEvent>? OnCaptured;

    /// <summary>
    /// 定期チェック時に差分率が計算された際に発火する診断イベント (オプション)
    /// </summary>
    public event Action<double, int>? OnDiffCalculated;

    /// <summary>
    /// モニター自動フォールバックやロック復帰などの状態変化時に発火するイベント
    /// </summary>
    public event Action<string>? OnScreenStatusMessage;

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
            _requestedMonitorIndex = options.MonitorIndex;
            _targetScreen = ScreenService.ResolveScreen(options.MonitorIndex);

            // outputDir/images ディレクトリを準備
            _imagesDirectory = Path.Combine(outputDir, "images");
            Directory.CreateDirectory(_imagesDirectory);

            _cts = new CancellationTokenSource();
            var nowUtc = DateTime.UtcNow;
            _startTime = nowUtc;
            _lastCaptureTime = DateTime.MinValue;
            LastCheckUtc = nowUtc;
            _capturedCount = 0;
            _lastSavedThumb = null;
            _lastSavedPhash = null;
            HealthStatus = "healthy";
            LastWarningMessage = null;
            FallbackCount = 0;
            IsCapturing = true;

            var ct = _cts.Token;

            // 1. 初回画面の即座撮影
            try
            {
                CaptureAndSaveInternal(_targetScreen.Bounds, "initial", 1.0, options.JpegQuality);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScreenCapture] 初回キャプチャ警告 (監視ループで自動リトライします): {ex.Message}");
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
            HealthStatus = "idle";
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

            try
            {
                RefreshTargetScreenBoundsIfNeeded();
                return CaptureAndSaveInternal(_targetScreen.Bounds, "manual", 1.0, jpegQuality);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScreenCapture] 手動撮影エラー (プライマリ画面で再試行): {ex.Message}");
                try
                {
                    _targetScreen = ScreenService.GetPrimaryScreen();
                    return CaptureAndSaveInternal(_targetScreen.Bounds, "manual", 1.0, jpegQuality);
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    /// <summary>
    /// モニター構成や解像度が動的に変わった場合に追従・フォールバックする
    /// </summary>
    private void RefreshTargetScreenBoundsIfNeeded()
    {
        try
        {
            var resolved = ScreenService.ResolveScreen(_requestedMonitorIndex);
            if (_targetScreen == null || _targetScreen.Bounds != resolved.Bounds || _targetScreen.Index != resolved.Index)
            {
                bool isFallback = _requestedMonitorIndex > 0 && resolved.Index != _requestedMonitorIndex;
                _targetScreen = resolved;

                if (isFallback)
                {
                    FallbackCount++;
                    string msg = $"指定モニター({_requestedMonitorIndex})が切断されたため、メイン画面({resolved.Name})へ自動切り替えしました。";
                    LastWarningMessage = msg;
                    Console.WriteLine($"[ScreenCapture] 🔄 {msg}");
                    OnScreenStatusMessage?.Invoke(msg);
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// 変化検知＆デバウンス＆自己修復監視ループ
    /// </summary>
    private async Task RunMonitoringLoopAsync(ScreenCaptureOptions options, CancellationToken ct)
    {
        bool wasScreenUnavailable = false;
        int consecutiveBlackFrames = 0;
        int consecutiveErrors = 0;
        int loopCounter = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.CheckIntervalMs, ct);
                LastCheckUtc = DateTime.UtcNow;
                loopCounter++;

                // 5秒ごとにディスプレイ構成・解像度の変化を動的チェック (プロジェクター抜線やドック着脱に対応)
                if (loopCounter % 5 == 0)
                {
                    RefreshTargetScreenBoundsIfNeeded();
                }

                var bounds = _targetScreen?.Bounds ?? ScreenService.GetPrimaryScreen().Bounds;

                // 1. 現在の画面をキャプチャしサムネイルおよびpHash生成
                byte[] currentThumb;
                ulong currentPhash;
                using (var currentBmp = CaptureScreenBitmap(bounds))
                {
                    currentThumb = ScreenDiffDetector.CreateGrayscaleThumbnail(currentBmp);
                    currentPhash = ScreenDiffDetector.ComputePhash(currentBmp);
                }

                // もし外部モニター指定中に完全な真っ黒画面が3回連続した場合は、モニター領域消失とみなしてメイン画面へフォールバック
                if (_requestedMonitorIndex > 0 && _targetScreen?.Index != 0 && ScreenDiffDetector.IsCompletelyBlack(currentThumb))
                {
                    consecutiveBlackFrames++;
                    if (consecutiveBlackFrames >= 3)
                    {
                        var primary = ScreenService.GetPrimaryScreen();
                        if (_targetScreen?.Bounds != primary.Bounds)
                        {
                            _targetScreen = primary;
                            FallbackCount++;
                            string msg = $"対象モニターが真っ黒(切断/消灯)のため、メイン画面({primary.Name})へ自動フォールバックしました。";
                            LastWarningMessage = msg;
                            Console.WriteLine($"[ScreenCapture] 🔄 {msg}");
                            OnScreenStatusMessage?.Invoke(msg);
                            consecutiveBlackFrames = 0;
                            continue;
                        }
                    }
                }
                else
                {
                    consecutiveBlackFrames = 0;
                }

                consecutiveErrors = 0;

                // ★ 画面ロック (Win+L) や UAC 暗転から復帰した直後の場合は、即座に復帰キーフレームを撮影！
                if (wasScreenUnavailable)
                {
                    wasScreenUnavailable = false;
                    HealthStatus = "healthy";
                    LastWarningMessage = null;
                    Console.WriteLine("[ScreenCapture] 🟢 画面ロック/暗転からの復帰を検知しました。キーフレームを撮影します。");

                    lock (_lock)
                    {
                        if (IsCapturing)
                        {
                            CaptureAndSaveInternal(bounds, "resume", 1.0, options.JpegQuality);
                        }
                    }
                    continue;
                }

                HealthStatus = "healthy";

                // 2. 差分計算 & 判定
                int elapsedSec = (int)(DateTime.UtcNow - _startTime).TotalSeconds;
                int timeSinceLastCapture = (int)(DateTime.UtcNow - _lastCaptureTime).TotalSeconds;

                bool shouldCapture = false;
                string reason = "";
                double reportDiff = 0.0;

                bool isPhashMode = string.Equals(options.DiffAlgorithm, "phash", StringComparison.OrdinalIgnoreCase);

                if (isPhashMode)
                {
                    // pHash (8x8 DCT) ハミング距離判定 (Pythonツール互換)
                    int hammingDist = _lastSavedPhash.HasValue
                        ? ScreenDiffDetector.ComputeHammingDistance(_lastSavedPhash.Value, currentPhash)
                        : 64;

                    reportDiff = hammingDist;
                    OnDiffCalculated?.Invoke((double)hammingDist / 64.0, elapsedSec);

                    if (hammingDist > options.PhashThreshold && timeSinceLastCapture >= options.MinIntervalSec)
                    {
                        shouldCapture = true;
                        reason = "phash";
                        Console.WriteLine($"[ScreenCapture] 📸 pHash画面変化検知! (ハミング距離: {hammingDist} > {options.PhashThreshold})");
                    }
                }
                else
                {
                    // 従来ハイブリッド方式 (全体平均輝度差＋変化面積比率)
                    double diff = ScreenDiffDetector.ComputeDifference(_lastSavedThumb, currentThumb);
                    reportDiff = diff;
                    OnDiffCalculated?.Invoke(diff, elapsedSec);

                    if (diff >= options.DiffThreshold && timeSinceLastCapture >= options.MinIntervalSec)
                    {
                        shouldCapture = true;
                        reason = "diff";
                        Console.WriteLine($"[ScreenCapture] 📸 ハイブリッド画面変化検知! (差分: {diff * 100:F1}% >= {options.DiffThreshold * 100:F1}%)");
                    }
                }

                // 3. 定期キーフレーム判定 (MaxIntervalSec > 0 の場合のみ)
                if (!shouldCapture && options.MaxIntervalSec > 0 && timeSinceLastCapture >= options.MaxIntervalSec)
                {
                    shouldCapture = true;
                    reason = "timer";
                    Console.WriteLine($"[ScreenCapture] ⏱️ 定期キーフレーム撮影 (前回撮影から {timeSinceLastCapture}秒経過)");
                }

                // 4. 撮影＆フル解像度保存
                if (shouldCapture && !ct.IsCancellationRequested)
                {
                    // デバウンス待機: アニメーション・ページめくり効果完了まで待つ (定期保存以外)
                    if (reason != "timer" && options.DebounceStabilityMs > 0)
                    {
                        await Task.Delay(options.DebounceStabilityMs, ct);
                    }

                    lock (_lock)
                    {
                        if (IsCapturing)
                        {
                            var latestBounds = _targetScreen?.Bounds ?? bounds;
                            CaptureAndSaveInternal(latestBounds, reason, reportDiff, options.JpegQuality);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is ExternalException || ex is ArgumentException)
            {
                // 画面ロック (Win+L)、UAC昇格プロンプト、スリープ遷移中などの GDI CopyFromScreen 例外
                consecutiveErrors++;
                wasScreenUnavailable = true;
                HealthStatus = "screen_locked";
                LastWarningMessage = "画面ロックまたはセキュア画面のため一時待機中 (解除後に自動再開します)";

                if (consecutiveErrors == 1)
                {
                    Console.WriteLine($"[ScreenCapture] ⏸️ 画面アクセス一時不可 (画面ロック/UAC等): {ex.Message} -> 解除まで安全待機します");
                }
                else if (consecutiveErrors == 3 && _targetScreen?.Index != 0)
                {
                    // モニター切断による無効座標エラーの可能性もあるため、プライマリ画面を再解決しておく
                    _targetScreen = ScreenService.GetPrimaryScreen();
                }

                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                HealthStatus = "warning";
                LastWarningMessage = $"画面監視一時エラー (自動リトライ中): {ex.Message}";
                Console.WriteLine($"[ScreenCapture] 監視中エラー ({consecutiveErrors}回目): {ex.Message}");

                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
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

        // 次回比較用サムネイル・pHashを更新
        _lastSavedThumb = ScreenDiffDetector.CreateGrayscaleThumbnail(bmp);
        _lastSavedPhash = ScreenDiffDetector.ComputePhash(bmp);

        var now = DateTime.Now;
        var elapsedSec = (int)(DateTime.UtcNow - _startTime).TotalSeconds;
        if (elapsedSec < 0) elapsedSec = 0;

        // Box 連携仕様準拠: screen_YYYYMMDD_HHMMSS.jpg
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
        int w = Math.Max(1, bounds.Width);
        int h = Math.Max(1, bounds.Height);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
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

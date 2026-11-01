using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WinMeetingRecorder.Screen;

/// <summary>
/// 画面のサムネイル生成および差分判定アルゴリズム
/// </summary>
[SupportedOSPlatform("windows")]
public static class ScreenDiffDetector
{
    public const int DefaultThumbnailWidth = 128;
    public const int DefaultThumbnailHeight = 72;

    /// <summary>
    /// キャプチャした元画像から低解像度グレースケール配列 (128x72 = 9216バイト) を生成します。
    /// GDI+バイリニア縮小により文字や細い線のノイズ（エイリアシング）を滑らかに除去します。
    /// </summary>
    public static byte[] CreateGrayscaleThumbnail(
        Bitmap srcBmp, 
        int width = DefaultThumbnailWidth, 
        int height = DefaultThumbnailHeight)
    {
        if (width <= 0) width = DefaultThumbnailWidth;
        if (height <= 0) height = DefaultThumbnailHeight;

        using var thumbBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(thumbBmp))
        {
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            g.SmoothingMode = SmoothingMode.HighSpeed;
            g.DrawImage(srcBmp, 0, 0, width, height);
        }

        BitmapData data = thumbBmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            int stride = data.Stride;
            int absStride = Math.Abs(stride);
            byte[] raw = new byte[absStride * height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);

            byte[] gray = new byte[width * height];
            int destIdx = 0;

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int px = rowStart + (x * 3); // Format24bppRgb: B, G, R
                    byte b = raw[px];
                    byte g = raw[px + 1];
                    byte r = raw[px + 2];

                    // ITU-R BT.601 グレースケール変換
                    gray[destIdx++] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                }
            }

            return gray;
        }
        finally
        {
            thumbBmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// 2つのサムネイル配列間の実効変化率 (0.0〜1.0) を算出します。
    /// 背景色が同じで文字や図表の一部だけが切り替わったスライド送りも取りこぼさないよう、
    /// 「全体平均輝度差」と「明確に変化したピクセル（輝度差>=28）の面積比率」のハイブリッド判定を行います。
    /// </summary>
    /// <param name="prev">前回のサムネイル配列</param>
    /// <param name="curr">現在のサムネイル配列</param>
    /// <returns>0.0 (同一) 〜 1.0 (最大変化)。前回のデータがない場合は 1.0</returns>
    public static double ComputeDifference(byte[]? prev, byte[]? curr)
    {
        if (prev == null || curr == null || prev.Length != curr.Length || prev.Length == 0)
        {
            return 1.0;
        }

        long totalDiff = 0;
        int significantChangedPixels = 0;

        for (int i = 0; i < prev.Length; i++)
        {
            int diff = Math.Abs(prev[i] - curr[i]);
            totalDiff += diff;

            // 輝度差が 28 (約11%) 以上の明確な変化ピクセルをカウント (時計の秒針や圧縮ノイズを除外しつつ文字・図表変化を捕捉)
            if (diff >= 28)
            {
                significantChangedPixels++;
            }
        }

        double meanDiff = (double)totalDiff / (prev.Length * 255.0);
        double activeAreaRatio = (double)significantChangedPixels / prev.Length;

        // 画面の 2.5% 以上の面積で明確な文字・図表書き換わりがある場合、
        // 背景が白/黒一色で meanDiff が薄まっていても確実に閾値(0.05)を超えるようハイブリッドスコアを返す
        double hybridScore = Math.Max(meanDiff, activeAreaRatio * 1.5);
        return Math.Min(1.0, hybridScore);
    }

    /// <summary>
    /// サムネイルが完全に真っ黒（モニター切断や消灯状態）かどうかを判定します
    /// </summary>
    public static bool IsCompletelyBlack(byte[]? thumb)
    {
        if (thumb == null || thumb.Length == 0) return true;

        int nonBlackCount = 0;
        for (int i = 0; i < thumb.Length; i++)
        {
            if (thumb[i] > 5)
            {
                nonBlackCount++;
                if (nonBlackCount > 10) return false;
            }
        }
        return true;
    }
}

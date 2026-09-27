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
    /// 2つのサムネイル配列間の平均ピクセル変化率 (0.0〜1.0) を算出します。
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
        for (int i = 0; i < prev.Length; i++)
        {
            totalDiff += Math.Abs(prev[i] - curr[i]);
        }

        return (double)totalDiff / (prev.Length * 255.0);
    }
}

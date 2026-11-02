using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WinMeetingRecorder.Screen;

/// <summary>
/// 画面のサムネイル生成および差分判定アルゴリズム (pHash 8x8 DCT / ハイブリッド差分)
/// </summary>
[SupportedOSPlatform("windows")]
public static class ScreenDiffDetector
{
    public const int DefaultThumbnailWidth = 128;
    public const int DefaultThumbnailHeight = 72;

    // pHash 用事前計算コサイン変換テーブル (k in [0..7], n in [0..31])
    private static readonly double[,] CosTable = InitializeCosTable();

    private static double[,] InitializeCosTable()
    {
        var table = new double[8, 32];
        for (int k = 0; k < 8; k++)
        {
            for (int n = 0; n < 32; n++)
            {
                table[k, n] = Math.Cos((Math.PI * k * (2 * n + 1)) / 64.0);
            }
        }
        return table;
    }

    /// <summary>
    /// 画像から 64ビットの知覚ハッシュ (pHash 8x8 DCT) を計算します。
    /// Python の imagehash.phash(image, hash_size=8, highfreq_factor=4) と互換性のあるアルゴリズムです。
    /// 1. 画像を 32x32 に縮小してグレースケール化
    /// 2. 2次元離散コサイン変換 (2D DCT-II) を計算
    /// 3. 左上 8x8 の低周波成分を抽出
    /// 4. 64成分の中央値 (Median) を基準に各ビットを 0/1 判定
    /// </summary>
    public static ulong ComputePhash(Bitmap srcBmp, int hashSize = 8, int highfreqFactor = 4)
    {
        int imgSize = hashSize * highfreqFactor; // 32
        if (imgSize != 32)
        {
            throw new ArgumentException("現在 hashSize=8, highfreqFactor=4 (imgSize=32) に最適化されています。");
        }

        // 1. 32x32 に高品質リサイズ
        using var thumbBmp = new Bitmap(imgSize, imgSize, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(thumbBmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            g.SmoothingMode = SmoothingMode.HighSpeed;
            g.DrawImage(srcBmp, 0, 0, imgSize, imgSize);
        }

        BitmapData data = thumbBmp.LockBits(
            new Rectangle(0, 0, imgSize, imgSize),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        double[,] pixels = new double[imgSize, imgSize];
        try
        {
            int stride = data.Stride;
            int absStride = Math.Abs(stride);
            byte[] raw = new byte[absStride * imgSize];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);

            for (int y = 0; y < imgSize; y++)
            {
                int rowStart = y * stride;
                for (int x = 0; x < imgSize; x++)
                {
                    int px = rowStart + (x * 3);
                    byte b = raw[px];
                    byte g = raw[px + 1];
                    byte r = raw[px + 2];
                    // ITU-R BT.601 グレースケール変換 (0.0〜255.0)
                    pixels[y, x] = (r * 299 + g * 587 + b * 114) / 1000.0;
                }
            }
        }
        finally
        {
            thumbBmp.UnlockBits(data);
        }

        // 2. 2D DCT-II (低周波 8x8 成分のみ抽出)
        // 横方向 1D DCT (32行 x 8列)
        double[,] intermediate = new double[32, 8];
        for (int y = 0; y < 32; y++)
        {
            for (int v = 0; v < 8; v++)
            {
                double sum = 0.0;
                for (int x = 0; x < 32; x++)
                {
                    sum += pixels[y, x] * CosTable[v, x];
                }
                intermediate[y, v] = sum * 2.0;
            }
        }

        // 縦方向 1D DCT (8行 x 8列)
        double[] values = new double[64];
        int idx = 0;
        for (int u = 0; u < 8; u++)
        {
            for (int v = 0; v < 8; v++)
            {
                double sum = 0.0;
                for (int y = 0; y < 32; y++)
                {
                    sum += intermediate[y, v] * CosTable[u, y];
                }
                values[idx++] = sum * 2.0;
            }
        }

        // 3. 中央値 (Median) を算出
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        double median = (sorted[31] + sorted[32]) / 2.0;

        // 4. 64ビットハッシュを生成 (各DCT成分 > median で 1)
        ulong hash = 0UL;
        for (int i = 0; i < 64; i++)
        {
            if (values[i] > median)
            {
                hash |= (1UL << i);
            }
        }

        return hash;
    }

    /// <summary>
    /// 2つの 64ビット知覚ハッシュ間のハミング距離 (異なるビット数: 0〜64) を算出します
    /// </summary>
    public static int ComputeHammingDistance(ulong hash1, ulong hash2)
    {
        return BitOperations.PopCount(hash1 ^ hash2);
    }

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

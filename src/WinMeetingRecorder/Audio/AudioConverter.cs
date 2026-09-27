using System.Diagnostics;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WinMeetingRecorder.Audio;

/// <summary>
/// 音声フォーマット変換サービス (WAV -> MP3 高圧縮化)
/// Windows Media Foundation (Windows標準機能・追加DLL不要) を使用
/// </summary>
public static class AudioConverter
{
    private static bool _initialized = false;
    private static readonly object _initLock = new();

    private static void EnsureInitialized()
    {
        if (!_initialized)
        {
            lock (_initLock)
            {
                if (!_initialized)
                {
                    MediaFoundationApi.Startup();
                    _initialized = true;
                }
            }
        }
    }

    /// <summary>
    /// サポートされているMP3エンコードフォーマット一覧をデバッグ出力する
    /// </summary>
    public static void ListSupportedMp3Formats()
    {
        EnsureInitialized();
        Console.WriteLine("[AudioConverter] サポートされている MP3 出力フォーマット:");
        var mediaTypes = MediaFoundationEncoder.GetOutputMediaTypes(AudioSubtypes.MFAudioFormat_MP3);
        int index = 0;
        foreach (var mt in mediaTypes)
        {
            try
            {
                int sampleRate = mt.SampleRate;
                int channels = mt.ChannelCount;
                int avgBytesPerSec = mt.AverageBytesPerSecond;
                int bitRate = avgBytesPerSec * 8;
                Console.WriteLine($"  [{index++}] {sampleRate}Hz, {channels}ch, {bitRate / 1000}kbps");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{index++}] 解析エラー: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// WAV ファイルを MP3 ファイルに圧縮変換する
    /// </summary>
    /// <param name="wavFilePath">入力WAVファイルのパス</param>
    /// <param name="mp3FilePath">出力MP3ファイルのパス</param>
    /// <param name="targetSampleRate">目標サンプリングレート (例: 24000 または 16000、0なら入力維持)</param>
    /// <param name="targetChannels">目標チャンネル数 (1: モノラル, 2: ステレオ, 0なら入力維持)</param>
    /// <param name="desiredBitRate">目標ビットレート (bps, 例: 64000 = 64kbps)</param>
    /// <returns>変換成功情報</returns>
    public static AudioConversionResult ConvertWavToMp3(
        string wavFilePath, 
        string mp3FilePath,
        int targetSampleRate = 24000,
        int targetChannels = 1,
        int desiredBitRate = 64000)
    {
        EnsureInitialized();

        if (!File.Exists(wavFilePath))
        {
            throw new FileNotFoundException("入力WAVファイルが見つかりません。", wavFilePath);
        }

        var sw = Stopwatch.StartNew();
        var origFileInfo = new FileInfo(wavFilePath);
        long origBytes = origFileInfo.Length;

        var outputDir = Path.GetDirectoryName(mp3FilePath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // 一時ファイルに出力してからリネーム（MediaFoundationは拡張子で判別するため .tmp.mp3 にする）
        var tempMp3Path = Path.Combine(outputDir ?? "", Path.GetFileNameWithoutExtension(mp3FilePath) + ".tmp.mp3");
        if (File.Exists(tempMp3Path))
        {
            File.Delete(tempMp3Path);
        }

        try
        {
            using (var reader = new AudioFileReader(wavFilePath))
            {
                IWaveProvider streamToEncode;

                bool needResample = targetSampleRate > 0 && reader.WaveFormat.SampleRate != targetSampleRate;
                bool needChannelChange = targetChannels > 0 && reader.WaveFormat.Channels != targetChannels;

                if (needResample || needChannelChange)
                {
                    ISampleProvider sampleProvider = reader;

                    // チャンネル変換 (Stereo -> Mono)
                    if (targetChannels == 1 && reader.WaveFormat.Channels == 2)
                    {
                        sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
                    }
                    // (Mono -> Stereo)
                    else if (targetChannels == 2 && reader.WaveFormat.Channels == 1)
                    {
                        sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
                    }

                    // サンプリングレート変換 (WDL Resampler)
                    if (needResample)
                    {
                        sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetSampleRate);
                    }

                    // 16bit PCM に変換
                    streamToEncode = new SampleToWaveProvider16(sampleProvider);
                }
                else
                {
                    streamToEncode = reader;
                }

                // MediaFoundation による MP3 エンコード実行
                try
                {
                    MediaFoundationEncoder.EncodeToMp3(streamToEncode, tempMp3Path, desiredBitRate);
                }
                catch (Exception mfEx)
                {
                    // もし特定サンプリングレート/モノラルのエンコードがMediaFoundationで拒否された場合、
                    // 入力フォーマットそのまま（48kHz stereo）で再試行するフォールバック
                    Console.WriteLine($"[AudioConverter] 最適化フォーマットでのエンコード失敗、フォールバック試行: {mfEx.Message}");
                    if (File.Exists(tempMp3Path)) File.Delete(tempMp3Path);

                    using var fallbackReader = new AudioFileReader(wavFilePath);
                    MediaFoundationEncoder.EncodeToMp3(fallbackReader, tempMp3Path, desiredBitRate);
                }
            }

            if (File.Exists(mp3FilePath))
            {
                File.Delete(mp3FilePath);
            }
            File.Move(tempMp3Path, mp3FilePath);

            sw.Stop();
            var mp3FileInfo = new FileInfo(mp3FilePath);
            long mp3Bytes = mp3FileInfo.Length;
            double compressionRatio = origBytes > 0 ? (1.0 - (double)mp3Bytes / origBytes) * 100.0 : 0;

            Console.WriteLine($"[AudioConverter] ✅ MP3変換完了: {origBytes / 1024.0:F1} KB -> {mp3Bytes / 1024.0:F1} KB (削減率 {compressionRatio:F1}%, 所要時間: {sw.ElapsedMilliseconds}ms)");

            return new AudioConversionResult(
                Success: true,
                InputFilePath: wavFilePath,
                OutputFilePath: mp3FilePath,
                OriginalSizeBytes: origBytes,
                CompressedSizeBytes: mp3Bytes,
                CompressionRatioPercent: compressionRatio,
                ElapsedMilliseconds: sw.ElapsedMilliseconds,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempMp3Path))
            {
                try { File.Delete(tempMp3Path); } catch { /* ignore */ }
            }

            Console.WriteLine($"[AudioConverter] ❌ MP3変換エラー: {ex.Message}");
            return new AudioConversionResult(
                Success: false,
                InputFilePath: wavFilePath,
                OutputFilePath: mp3FilePath,
                OriginalSizeBytes: origBytes,
                CompressedSizeBytes: 0,
                CompressionRatioPercent: 0,
                ElapsedMilliseconds: sw.ElapsedMilliseconds,
                ErrorMessage: ex.Message);
        }
    }
}

public record AudioConversionResult(
    bool Success,
    string InputFilePath,
    string OutputFilePath,
    long OriginalSizeBytes,
    long CompressedSizeBytes,
    double CompressionRatioPercent,
    long ElapsedMilliseconds,
    string? ErrorMessage);

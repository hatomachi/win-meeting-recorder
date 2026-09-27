using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WinMeetingRecorder.Audio;

public class AudioEngine : IDisposable
{
    private const int TargetSampleRate = 48000;
    private const int TargetChannels = 2;

    private WasapiLoopbackCapture? _loopbackCapture;
    private WasapiCapture? _micCapture;

    private BufferedWaveProvider? _loopbackBuffer;
    private BufferedWaveProvider? _micBuffer;

    private MixingSampleProvider? _mixer;
    private WaveFileWriter? _waveWriter;

    private CancellationTokenSource? _recordingCts;
    private Task? _recordingTask;

    public bool IsRecording { get; private set; }
    public string? CurrentOutputFile { get; private set; }

    public float LoopbackVolume { get; set; } = 0.85f;
    public float MicVolume { get; set; } = 0.85f;

    /// <summary>
    /// 録音を開始する
    /// </summary>
    /// <param name="outputFilePath">出力WAVファイルのフルパス</param>
    /// <param name="playbackDeviceId">対象スピーカーのデバイスID (nullなら既定)</param>
    /// <param name="captureDeviceId">対象マイクのデバイスID (nullなら既定)</param>
    public void StartRecording(string outputFilePath, string? playbackDeviceId = null, string? captureDeviceId = null)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("既に録音中です。");
        }

        CurrentOutputFile = outputFilePath;
        var outputDir = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        using var enumerator = new MMDeviceEnumerator();

        // 1. スピーカー (WASAPI Loopback) の初期化
        MMDevice? playbackDevice = null;
        if (!string.IsNullOrEmpty(playbackDeviceId))
        {
            try { playbackDevice = enumerator.GetDevice(playbackDeviceId); } catch { /* ignore */ }
        }
        _loopbackCapture = playbackDevice != null 
            ? new WasapiLoopbackCapture(playbackDevice) 
            : new WasapiLoopbackCapture();

        _loopbackBuffer = new BufferedWaveProvider(_loopbackCapture.WaveFormat)
        {
            ReadFully = true,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5)
        };

        _loopbackCapture.DataAvailable += (s, a) =>
        {
            _loopbackBuffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
        };

        // 2. マイク (WASAPI Capture) の初期化
        MMDevice? micDevice = null;
        if (!string.IsNullOrEmpty(captureDeviceId))
        {
            try { micDevice = enumerator.GetDevice(captureDeviceId); } catch { /* ignore */ }
        }
        else
        {
            try
            {
                micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }
            catch
            {
                try { micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia); } catch { /* ignore */ }
            }
        }

        if (micDevice != null)
        {
            _micCapture = new WasapiCapture(micDevice);
            _micBuffer = new BufferedWaveProvider(_micCapture.WaveFormat)
            {
                ReadFully = true,
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(5)
            };

            _micCapture.DataAvailable += (s, a) =>
            {
                _micBuffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
            };
        }

        // 3. ミキサー (MixingSampleProvider) の構築
        var targetWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, TargetChannels);
        _mixer = new MixingSampleProvider(targetWaveFormat)
        {
            ReadFully = true
        };

        // スピーカー入力の変換 (ISampleProvider -> WDL Resampler -> Stereo -> Volume)
        var loopbackSample = ConvertToTargetSampleProvider(_loopbackBuffer, TargetSampleRate, TargetChannels, LoopbackVolume);
        _mixer.AddMixerInput(loopbackSample);

        // マイク入力の変換
        if (_micBuffer != null)
        {
            var micSample = ConvertToTargetSampleProvider(_micBuffer, TargetSampleRate, TargetChannels, MicVolume);
            _mixer.AddMixerInput(micSample);
        }

        // 4. WAVライターの初期化 (16bit PCM に変換してファイルサイズ節約)
        var waveProvider16 = new SampleToWaveProvider16(_mixer);
        _waveWriter = new WaveFileWriter(outputFilePath, waveProvider16.WaveFormat);

        // 5. 録音ループの開始
        _recordingCts = new CancellationTokenSource();
        var token = _recordingCts.Token;

        _loopbackCapture.StartRecording();
        _micCapture?.StartRecording();
        IsRecording = true;

        _recordingTask = Task.Run(async () =>
        {
            // 20ms ごとに読み取り (48000Hz * 2ch * 2bytes * 0.02s = 3840 bytes)
            var bufferSize = targetWaveFormat.SampleRate * targetWaveFormat.Channels * 2 / 50;
            var buffer = new byte[bufferSize];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var read = waveProvider16.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        _waveWriter.Write(buffer, 0, read);
                    }
                    await Task.Delay(20, token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常キャンセル
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioEngine Error] {ex.Message}");
            }
        }, token);
    }

    /// <summary>
    /// 任意のバッファプロバイダを、目標サンプリングレート・2ch・指定音量のISampleProviderに変換する
    /// </summary>
    private static ISampleProvider ConvertToTargetSampleProvider(
        IWaveProvider provider,
        int targetSampleRate,
        int targetChannels,
        float volume)
    {
        ISampleProvider sampleProvider = provider.ToSampleProvider();

        // 1. サンプリングレート変換 (WDL Resampler)
        if (provider.WaveFormat.SampleRate != targetSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetSampleRate);
        }

        // 2. チャンネル数変換 (Mono -> Stereo)
        if (provider.WaveFormat.Channels == 1 && targetChannels == 2)
        {
            sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
        }
        else if (provider.WaveFormat.Channels > targetChannels)
        {
            // 多チャンネル -> Stereo (必要ならダウンミックス)
        }

        // 3. ボリューム調整 (クリッピング防止)
        if (Math.Abs(volume - 1.0f) > 0.01f)
        {
            sampleProvider = new VolumeSampleProvider(sampleProvider) { Volume = volume };
        }

        return sampleProvider;
    }

    /// <summary>
    /// 録音を停止し、ファイルを確定する
    /// </summary>
    public async Task StopRecordingAsync()
    {
        if (!IsRecording) return;

        IsRecording = false;

        try
        {
            _loopbackCapture?.StopRecording();
            _micCapture?.StopRecording();

            if (_recordingCts != null)
            {
                _recordingCts.Cancel();
                if (_recordingTask != null)
                {
                    try { await _recordingTask; } catch { /* ignore */ }
                }
            }

            _waveWriter?.Flush();
            _waveWriter?.Dispose();
            _waveWriter = null;

            _loopbackCapture?.Dispose();
            _loopbackCapture = null;

            _micCapture?.Dispose();
            _micCapture = null;
        }
        finally
        {
            _recordingCts?.Dispose();
            _recordingCts = null;
        }
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            StopRecordingAsync().GetAwaiter().GetResult();
        }
        _waveWriter?.Dispose();
        _loopbackCapture?.Dispose();
        _micCapture?.Dispose();
        _recordingCts?.Dispose();
    }
}

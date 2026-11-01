using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WinMeetingRecorder.Audio;

public class AudioEngine : IDisposable
{
    private const int TargetSampleRate = 48000;
    private const int TargetChannels = 2;

    private readonly object _mixerLock = new();

    private string? _requestedPlaybackDeviceId;
    private string? _requestedCaptureDeviceId;

    private WasapiLoopbackCapture? _loopbackCapture;
    private WasapiCapture? _micCapture;

    private BufferedWaveProvider? _loopbackBuffer;
    private BufferedWaveProvider? _micBuffer;

    private ISampleProvider? _loopbackSampleProvider;
    private ISampleProvider? _micSampleProvider;

    private MixingSampleProvider? _mixer;
    private SampleToWaveProvider16? _waveProvider16;
    private WaveFileWriter? _waveWriter;

    private Stopwatch? _stopwatch;
    private long _totalBytesWritten;
    private int _bytesPerSecond;

    private CancellationTokenSource? _recordingCts;
    private Task? _recordingTask;

    private volatile bool _needsLoopbackReconnect;
    private volatile bool _needsMicReconnect;
    private DateTime _lastLoopbackReconnectAttempt = DateTime.MinValue;
    private DateTime _lastMicReconnectAttempt = DateTime.MinValue;
    private DateTime _lastFlushTime = DateTime.MinValue;

    public bool IsRecording { get; private set; }
    public string? CurrentOutputFile { get; private set; }

    /// <summary>
    /// 現在の合成音量レベル (0.0f ~ 1.0f)。タスクトレイ等の波形アニメーション表示用
    /// </summary>
    public float CurrentAudioLevel { get; private set; }

    public float LoopbackVolume { get; set; } = 0.85f;
    public float MicVolume { get; set; } = 0.85f;

    // 健全性・監視用テレメトリ
    public DateTime LastAudioWriteUtc { get; private set; } = DateTime.MinValue;
    public DateTime LastNonSilentUtc { get; private set; } = DateTime.MinValue;
    public int ReconnectCount { get; private set; }
    public string ActivePlaybackDeviceName { get; private set; } = "";
    public string ActiveMicDeviceName { get; private set; } = "";
    public string? LastWarningMessage { get; private set; }

    /// <summary>
    /// デバイス切断からの自動再接続成功時に発火するイベント
    /// </summary>
    public event Action<string>? OnDeviceReconnected;

    /// <summary>
    /// 録音中の警告発生時に発火するイベント
    /// </summary>
    public event Action<string>? OnAudioWarning;

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
        _requestedPlaybackDeviceId = playbackDeviceId;
        _requestedCaptureDeviceId = captureDeviceId;

        var outputDir = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // ミキサー (MixingSampleProvider) の構築
        var targetWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, TargetChannels);
        _mixer = new MixingSampleProvider(targetWaveFormat)
        {
            ReadFully = true
        };

        // 1. スピーカー (WASAPI Loopback) の初期化
        InitializeLoopbackCapture(playbackDeviceId, isReconnect: false);

        // 2. マイク (WASAPI Capture) の初期化
        InitializeMicCapture(captureDeviceId, isReconnect: false);

        // 3. WAVライターの初期化 (16bit PCM に変換: 48000Hz * 2ch * 2bytes = 192,000 bytes/sec)
        _waveProvider16 = new SampleToWaveProvider16(_mixer);
        _waveWriter = new WaveFileWriter(outputFilePath, _waveProvider16.WaveFormat);

        _bytesPerSecond = _waveProvider16.WaveFormat.AverageBytesPerSecond; // 192000
        _totalBytesWritten = 0;
        _stopwatch = Stopwatch.StartNew();

        var nowUtc = DateTime.UtcNow;
        LastAudioWriteUtc = nowUtc;
        LastNonSilentUtc = nowUtc;
        _lastFlushTime = nowUtc;
        ReconnectCount = 0;
        LastWarningMessage = null;
        _needsLoopbackReconnect = false;
        _needsMicReconnect = false;

        // 4. 録音ループの開始 (自己修復・実時間追従ドレイン方式)
        _recordingCts = new CancellationTokenSource();
        var token = _recordingCts.Token;

        IsRecording = true;

        try
        {
            _loopbackCapture?.StartRecording();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioEngine] スピーカー録音開始警告 (自動再接続待機): {ex.Message}");
            _needsLoopbackReconnect = true;
        }

        try
        {
            _micCapture?.StartRecording();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioEngine] マイク録音開始警告 (自動再接続待機): {ex.Message}");
            _needsMicReconnect = true;
        }

        _recordingTask = Task.Run(async () =>
        {
            var chunk = new byte[19200]; // 0.1秒単位
            int consecutiveErrors = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_stopwatch == null || _waveWriter == null || _waveProvider16 == null)
                    {
                        await Task.Delay(20, token);
                        continue;
                    }

                    // デバイス切断・停止検知時の自動再接続 (自己修復)
                    CheckAndReconnectDevicesIfNeeded();

                    // 経過した実時間に基づいて、現時点で書き込まれているべきバイト数を計算
                    long targetBytes = (long)(_stopwatch.Elapsed.TotalSeconds * _bytesPerSecond);
                    long bytesToRead = targetBytes - _totalBytesWritten;

                    // 万が一大幅な遅延が発生した場合の異常バースト防止 (最大5秒分までに制限)
                    long maxBurstBytes = _bytesPerSecond * 5L;
                    if (bytesToRead > maxBurstBytes)
                    {
                        bytesToRead = maxBurstBytes;
                        _totalBytesWritten = targetBytes - maxBurstBytes;
                    }

                    while (bytesToRead > 0 && !token.IsCancellationRequested)
                    {
                        int toRead = (int)Math.Min(bytesToRead, chunk.Length);
                        int read;
                        lock (_mixerLock)
                        {
                            read = _waveProvider16.Read(chunk, 0, toRead);
                        }
                        if (read <= 0) break;

                        // リアルタイム音量ピークの算出 (16bit PCM Stereo)
                        float maxPeak = 0f;
                        for (int i = 0; i < read - 1; i += 2)
                        {
                            short sample = (short)(chunk[i] | (chunk[i + 1] << 8));
                            float abs = Math.Abs(sample / 32768f);
                            if (abs > maxPeak) maxPeak = abs;
                        }

                        // 瞬間ピークを反映し、適度な減衰でなめらかに追従
                        CurrentAudioLevel = Math.Max(maxPeak, CurrentAudioLevel * 0.85f);

                        var utcNow = DateTime.UtcNow;
                        if (maxPeak > 0.005f)
                        {
                            LastNonSilentUtc = utcNow;
                        }

                        _waveWriter.Write(chunk, 0, read);
                        _totalBytesWritten += read;
                        bytesToRead -= read;
                        LastAudioWriteUtc = utcNow;

                        // 5秒ごとに定期Flush (万が一のPC電源断・クラッシュ時でもデータを失わない)
                        if ((utcNow - _lastFlushTime).TotalSeconds >= 5.0)
                        {
                            _waveWriter.Flush();
                            _lastFlushTime = utcNow;
                        }
                    }

                    consecutiveErrors = 0;
                    await Task.Delay(15, token);
                }
                catch (OperationCanceledException)
                {
                    // 正常キャンセル
                    break;
                }
                catch (Exception ex)
                {
                    // ★ 重要: 1回のエラーで録音ループを絶対に死なせない (自己修復ループ)
                    consecutiveErrors++;
                    LastWarningMessage = $"音声処理一時エラー (自動リトライ中): {ex.Message}";
                    Console.WriteLine($"[AudioEngine Self-Heal] ループ内エラー検知 ({consecutiveErrors}回目): {ex.Message}");

                    if (consecutiveErrors == 1 || consecutiveErrors % 20 == 0)
                    {
                        OnAudioWarning?.Invoke(LastWarningMessage);
                    }

                    try
                    {
                        await Task.Delay(Math.Min(100 * consecutiveErrors, 1000), token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }, token);
    }

    /// <summary>
    /// スピーカー (WASAPI Loopback) キャプチャを初期化しミキサーへ接続する
    /// </summary>
    private void InitializeLoopbackCapture(string? playbackDeviceId, bool isReconnect)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? playbackDevice = null;

        if (!string.IsNullOrEmpty(playbackDeviceId))
        {
            try
            {
                var candidate = enumerator.GetDevice(playbackDeviceId);
                if (candidate != null && candidate.State == DeviceState.Active)
                {
                    playbackDevice = candidate;
                }
            }
            catch { /* 指定デバイス消失時は既定デバイスへ自動フォールバック */ }
        }

        if (playbackDevice == null)
        {
            try
            {
                playbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch
            {
                try { playbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console); } catch { /* ignore */ }
            }
        }

        var newCapture = playbackDevice != null
            ? new WasapiLoopbackCapture(playbackDevice)
            : new WasapiLoopbackCapture();

        ActivePlaybackDeviceName = playbackDevice?.FriendlyName ?? "既定スピーカー";

        var newBuffer = new BufferedWaveProvider(newCapture.WaveFormat)
        {
            ReadFully = true,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(30)
        };

        newCapture.DataAvailable += (s, a) =>
        {
            if (a.BytesRecorded > 0)
            {
                newBuffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
            }
        };

        newCapture.RecordingStopped += (s, a) =>
        {
            if (IsRecording && !(_recordingCts?.IsCancellationRequested ?? true))
            {
                Console.WriteLine($"[AudioEngine] ⚠️ スピーカーデバイス停止検知 ({ActivePlaybackDeviceName}): {a.Exception?.Message ?? "デバイス変更/切断"}");
                _needsLoopbackReconnect = true;
            }
        };

        var newSampleProvider = ConvertToTargetSampleProvider(newBuffer, TargetSampleRate, TargetChannels, LoopbackVolume);

        lock (_mixerLock)
        {
            if (_mixer != null)
            {
                if (_loopbackSampleProvider != null)
                {
                    _mixer.RemoveMixerInput(_loopbackSampleProvider);
                }
                _mixer.AddMixerInput(newSampleProvider);
            }
            _loopbackSampleProvider = newSampleProvider;
            _loopbackBuffer = newBuffer;
            _loopbackCapture = newCapture;
        }

        if (isReconnect && IsRecording)
        {
            _loopbackCapture.StartRecording();
        }
    }

    /// <summary>
    /// マイク (WASAPI Capture) を初期化しミキサーへ接続する
    /// </summary>
    private void InitializeMicCapture(string? captureDeviceId, bool isReconnect)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? micDevice = null;

        if (!string.IsNullOrEmpty(captureDeviceId))
        {
            try
            {
                var candidate = enumerator.GetDevice(captureDeviceId);
                if (candidate != null && candidate.State == DeviceState.Active)
                {
                    micDevice = candidate;
                }
            }
            catch { /* 指定マイク消失時は既定マイクへ自動フォールバック */ }
        }

        if (micDevice == null)
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

        if (micDevice == null)
        {
            ActiveMicDeviceName = "(マイク未接続)";
            return;
        }

        var newCapture = new WasapiCapture(micDevice);
        ActiveMicDeviceName = micDevice.FriendlyName;

        var newBuffer = new BufferedWaveProvider(newCapture.WaveFormat)
        {
            ReadFully = true,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(30)
        };

        newCapture.DataAvailable += (s, a) =>
        {
            if (a.BytesRecorded > 0)
            {
                newBuffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
            }
        };

        newCapture.RecordingStopped += (s, a) =>
        {
            if (IsRecording && !(_recordingCts?.IsCancellationRequested ?? true))
            {
                Console.WriteLine($"[AudioEngine] ⚠️ マイクデバイス停止検知 ({ActiveMicDeviceName}): {a.Exception?.Message ?? "デバイス変更/切断"}");
                _needsMicReconnect = true;
            }
        };

        var newSampleProvider = ConvertToTargetSampleProvider(newBuffer, TargetSampleRate, TargetChannels, MicVolume);

        lock (_mixerLock)
        {
            if (_mixer != null)
            {
                if (_micSampleProvider != null)
                {
                    _mixer.RemoveMixerInput(_micSampleProvider);
                }
                _mixer.AddMixerInput(newSampleProvider);
            }
            _micSampleProvider = newSampleProvider;
            _micBuffer = newBuffer;
            _micCapture = newCapture;
        }

        if (isReconnect && IsRecording)
        {
            _micCapture.StartRecording();
        }
    }

    /// <summary>
    /// デバイス切断・異常停止を検知した場合に3秒クールダウンで自動再接続を試みる
    /// </summary>
    private void CheckAndReconnectDevicesIfNeeded()
    {
        var now = DateTime.UtcNow;

        // 1. スピーカー (Loopback) の死活チェックと自動再接続
        bool loopbackStopped = _needsLoopbackReconnect ||
                               (_loopbackCapture != null && _loopbackCapture.CaptureState == CaptureState.Stopped);

        if (loopbackStopped && (now - _lastLoopbackReconnectAttempt).TotalSeconds >= 3.0)
        {
            _lastLoopbackReconnectAttempt = now;
            _needsLoopbackReconnect = false;

            try
            {
                try { _loopbackCapture?.Dispose(); } catch { /* ignore */ }
                _loopbackCapture = null;

                InitializeLoopbackCapture(_requestedPlaybackDeviceId, isReconnect: true);
                ReconnectCount++;
                string msg = $"スピーカー音声を自動再接続しました ({ActivePlaybackDeviceName})";
                Console.WriteLine($"[AudioEngine] 🔄 {msg}");
                OnDeviceReconnected?.Invoke(msg);
            }
            catch (Exception ex)
            {
                _needsLoopbackReconnect = true;
                Console.WriteLine($"[AudioEngine] スピーカー再接続待機中: {ex.Message}");
            }
        }

        // 2. マイク (Capture) の死活チェックと自動再接続
        bool micStopped = _needsMicReconnect ||
                          (_micCapture != null && _micCapture.CaptureState == CaptureState.Stopped);

        if (micStopped && (now - _lastMicReconnectAttempt).TotalSeconds >= 3.0)
        {
            _lastMicReconnectAttempt = now;
            _needsMicReconnect = false;

            try
            {
                try { _micCapture?.Dispose(); } catch { /* ignore */ }
                _micCapture = null;

                InitializeMicCapture(_requestedCaptureDeviceId, isReconnect: true);
                if (_micCapture != null)
                {
                    ReconnectCount++;
                    string msg = $"マイク入力を自動再接続しました ({ActiveMicDeviceName})";
                    Console.WriteLine($"[AudioEngine] 🔄 {msg}");
                    OnDeviceReconnected?.Invoke(msg);
                }
            }
            catch (Exception ex)
            {
                _needsMicReconnect = true;
                Console.WriteLine($"[AudioEngine] マイク再接続待機中: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 外部Watchdogまたは手動リカバリから音声エンジンの強制リフレッシュを要求する場合に呼び出す
    /// </summary>
    public void RequestDeviceRefresh()
    {
        if (!IsRecording) return;
        _lastLoopbackReconnectAttempt = DateTime.MinValue;
        _lastMicReconnectAttempt = DateTime.MinValue;
        _needsLoopbackReconnect = true;
        _needsMicReconnect = true;
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
        CurrentAudioLevel = 0f;

        try
        {
            if (_recordingCts != null)
            {
                _recordingCts.Cancel();
            }

            try { _loopbackCapture?.StopRecording(); } catch { /* ignore */ }
            try { _micCapture?.StopRecording(); } catch { /* ignore */ }

            if (_recordingTask != null)
            {
                try { await _recordingTask; } catch { /* ignore */ }
            }

            // 停止時点の実時間までの未書き込みサンプルをすべてフラッシュ
            if (_stopwatch != null)
            {
                _stopwatch.Stop();
                if (_waveWriter != null && _waveProvider16 != null)
                {
                    long finalTargetBytes = (long)(_stopwatch.Elapsed.TotalSeconds * _bytesPerSecond);
                    long remainingBytes = Math.Min(finalTargetBytes - _totalBytesWritten, _bytesPerSecond * 2L);
                    var flushBuffer = new byte[19200];
                    while (remainingBytes > 0)
                    {
                        int toRead = (int)Math.Min(remainingBytes, flushBuffer.Length);
                        int read;
                        lock (_mixerLock)
                        {
                            read = _waveProvider16.Read(flushBuffer, 0, toRead);
                        }
                        if (read <= 0) break;

                        _waveWriter.Write(flushBuffer, 0, read);
                        _totalBytesWritten += read;
                        remainingBytes -= read;
                    }
                }
            }

            _waveWriter?.Flush();
            _waveWriter?.Dispose();
            _waveWriter = null;

            try { _loopbackCapture?.Dispose(); } catch { /* ignore */ }
            _loopbackCapture = null;

            try { _micCapture?.Dispose(); } catch { /* ignore */ }
            _micCapture = null;
        }
        finally
        {
            _recordingCts?.Dispose();
            _recordingCts = null;
            _stopwatch = null;
            _loopbackSampleProvider = null;
            _micSampleProvider = null;
        }
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            StopRecordingAsync().GetAwaiter().GetResult();
        }
        _waveWriter?.Dispose();
        try { _loopbackCapture?.Dispose(); } catch { /* ignore */ }
        try { _micCapture?.Dispose(); } catch { /* ignore */ }
        _recordingCts?.Dispose();
    }
}

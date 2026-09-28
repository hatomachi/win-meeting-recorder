using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMeetingRecorder.Config;

namespace WinMeetingRecorder.Whisper;

/// <summary>
/// Whisper 接続テスト結果
/// </summary>
public record WhisperTestResult(
    bool Success,
    string Message,
    string? Endpoint = null,
    int? StatusCode = null);

/// <summary>
/// Whisper 文字起こし実行結果
/// </summary>
public record WhisperTranscribeResult(
    bool Success,
    string Message,
    string? Text = null,
    int SegmentCount = 0,
    double Duration = 0,
    long ElapsedMilliseconds = 0,
    string? TranscriptFilePath = null,
    WhisperTranscriptionResponse? Response = null);

/// <summary>
/// OpenAI Whisper API (verbose_json) レスポンス定義
/// </summary>
public class WhisperTranscriptionResponse
{
    [JsonPropertyName("task")]
    public string Task { get; set; } = "transcribe";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "japanese";

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("segments")]
    public List<WhisperSegmentItem> Segments { get; set; } = new();
}

/// <summary>
/// 文字起こしセグメント (発話ごとのタイムスタンプ付きテキスト)
/// </summary>
public class WhisperSegmentItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double End { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

/// <summary>
/// OpenAI Whisper API 互換連携サービス
/// </summary>
public class WhisperService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// 設定内容に基づいて HttpClient を生成します
    /// </summary>
    private HttpClient CreateHttpClient(WhisperConfig config)
    {
        var handler = new HttpClientHandler
        {
            // 社内環境の自己署名SSL許可
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        if (config.BypassProxy)
        {
            // 社内Whisperサーバー(IP直接)への接続は、OS/環境変数のプロキシ設定（PACやHTTP_PROXY等）に左右されず直接接続する。
            // 社内IPであってもプロキシのバイパス例外に含まれていないとプロキシ経由となり到達できずに失敗するケースがあるため。
            handler.UseProxy = false;
            handler.Proxy = null;
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30) // 大容量・長時間音声の推論待ち対応
        };

        var baseUrl = config.BaseUrl.Trim().TrimEnd('/') + "/";
        client.BaseAddress = new Uri(baseUrl);

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        }

        return client;
    }

    /// <summary>
    /// Whisper API サーバーへの疎通確認を行います
    /// </summary>
    public async Task<WhisperTestResult> TestConnectionAsync(WhisperConfig config)
    {
        if (!config.IsConfigured)
        {
            return new WhisperTestResult(false, "Whisper API サーバーの Base URL が設定されていません。");
        }

        try
        {
            using var client = CreateHttpClient(config);
            var endpoint = config.BaseUrl.Trim().TrimEnd('/');

            // 1. まず /health を試す
            try
            {
                var healthResp = await client.GetAsync("health");
                if (healthResp.IsSuccessStatusCode)
                {
                    return new WhisperTestResult(true, "接続成功: Whisper API (/health) と通信できました。", endpoint, (int)healthResp.StatusCode);
                }
            }
            catch { /* 次のフォールバックへ */ }

            // 2. /v1/models を試す
            try
            {
                var modelsResp = await client.GetAsync("v1/models");
                if (modelsResp.IsSuccessStatusCode)
                {
                    return new WhisperTestResult(true, "接続成功: Whisper API (/v1/models) と通信できました。", endpoint, (int)modelsResp.StatusCode);
                }
            }
            catch { /* 次のフォールバックへ */ }

            // 3. ルート GET を試す
            var rootResp = await client.GetAsync("");
            // 200〜499番台（401 Unauthorized や 404 Not Found でもサーバー自体には届いている）
            int code = (int)rootResp.StatusCode;
            if (code >= 200 && code < 500)
            {
                return new WhisperTestResult(true, $"接続成功: サーバーからの応答を確認しました (HTTP {code})。", endpoint, code);
            }

            return new WhisperTestResult(false, $"サーバーエラー (HTTP {code} {rootResp.ReasonPhrase})", endpoint, code);
        }
        catch (Exception ex)
        {
            return new WhisperTestResult(false, $"通信例外: {ex.Message}", config.BaseUrl);
        }
    }

    /// <summary>
    /// 音声ファイルを Whisper API に送信し、文字起こし (verbose_json) を実行します
    /// </summary>
    /// <param name="audioFilePath">対象の音声ファイル (MP3 または WAV)</param>
    /// <param name="config">Whisper設定</param>
    /// <param name="outputJsonPath">結果を保存する transcript.json パス (nullなら自動決定)</param>
    public async Task<WhisperTranscribeResult> TranscribeAudioAsync(
        string audioFilePath, 
        WhisperConfig config, 
        string? outputJsonPath = null)
    {
        if (!File.Exists(audioFilePath))
        {
            return new WhisperTranscribeResult(false, $"音声ファイルが見つかりません: {audioFilePath}");
        }

        if (!config.IsConfigured)
        {
            return new WhisperTranscribeResult(false, "Whisper API の設定 (BaseUrl) が未完了です。");
        }

        var sw = Stopwatch.StartNew();
        var fileInfo = new FileInfo(audioFilePath);
        Console.WriteLine($"[WhisperService] 🎙️ 文字起こし開始: {fileInfo.Name} ({fileInfo.Length / 1024.0:F1} KB) -> {config.BaseUrl} (Model: {config.Model})");

        outputJsonPath ??= Path.Combine(Path.GetDirectoryName(audioFilePath) ?? "", "transcript.json");

        try
        {
            using var client = CreateHttpClient(config);
            using var content = new MultipartFormDataContent();

            // 音声ファイルストリームの追加
            var fileStream = File.OpenRead(audioFilePath);
            var isMp3 = audioFilePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
            var mediaType = isMp3 ? "audio/mpeg" : "audio/wav";
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            content.Add(streamContent, "file", Path.GetFileName(audioFilePath));

            // モデル名
            var modelName = string.IsNullOrWhiteSpace(config.Model) ? "whisper-1" : config.Model.Trim();
            content.Add(new StringContent(modelName), "model");

            // 言語
            var lang = string.IsNullOrWhiteSpace(config.Language) ? "ja" : config.Language.Trim();
            content.Add(new StringContent(lang), "language");

            // verbose_json (タイムスタンプ付きセグメント取得用)
            content.Add(new StringContent("verbose_json"), "response_format");

            // プロンプト (任意)
            if (!string.IsNullOrWhiteSpace(config.Prompt))
            {
                content.Add(new StringContent(config.Prompt.Trim()), "prompt");
            }

            var requestUrl = "v1/audio/transcriptions";
            var response = await client.PostAsync(requestUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[WhisperService] ❌ API呼び出しエラー (HTTP {(int)response.StatusCode} {response.ReasonPhrase}): {responseBody}");
                return new WhisperTranscribeResult(
                    false,
                    $"Whisper API エラー (HTTP {(int)response.StatusCode} {response.ReasonPhrase}): {responseBody}",
                    ElapsedMilliseconds: sw.ElapsedMilliseconds);
            }

            // verbose_json パース
            var transcript = JsonSerializer.Deserialize<WhisperTranscriptionResponse>(responseBody, JsonOptions);
            if (transcript == null)
            {
                return new WhisperTranscribeResult(
                    false,
                    "APIレスポンスのパースに失敗しました。",
                    ElapsedMilliseconds: sw.ElapsedMilliseconds);
            }

            // transcript.json として保存
            var dir = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(outputJsonPath, JsonSerializer.Serialize(transcript, JsonOptions), Encoding.UTF8);

            // webapp-obsidian (MINUTES_GUIDE) 互換の transcript.yaml も同時に保存
            var outputYamlPath = Path.Combine(dir ?? "", "transcript.yaml");
            var yamlContent = ToYamlPatternA(transcript.Segments);
            await File.WriteAllTextAsync(outputYamlPath, yamlContent, Encoding.UTF8);

            Console.WriteLine($"[WhisperService] ✅ 文字起こし成功: {transcript.Segments.Count} セグメント (全体: {transcript.Text.Length}文字, 所要時間: {sw.ElapsedMilliseconds}ms)");
            Console.WriteLine($"[WhisperService] 💾 保存先: {outputJsonPath} & {outputYamlPath}");

            return new WhisperTranscribeResult(
                Success: true,
                Message: "文字起こしが完了しました。",
                Text: transcript.Text,
                SegmentCount: transcript.Segments.Count,
                Duration: transcript.Duration,
                ElapsedMilliseconds: sw.ElapsedMilliseconds,
                TranscriptFilePath: outputJsonPath,
                Response: transcript);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.WriteLine($"[WhisperService] ❌ 文字起こし例外: {ex.Message}");
            return new WhisperTranscribeResult(
                Success: false,
                Message: $"文字起こし通信例外: {ex.Message}",
                ElapsedMilliseconds: sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Whisper セグメント一覧を webapp-obsidian (MINUTES_GUIDE Pattern A) 準拠の YAML 文字列へ変換します
    /// </summary>
    public static string ToYamlPatternA(IEnumerable<WhisperSegmentItem> segments)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            sb.AppendLine($"- start: {seg.Start.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"  end: {seg.End.ToString("F2", CultureInfo.InvariantCulture)}");
            var cleanText = (seg.Text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
            sb.AppendLine($"  text: \"{cleanText}\"");
        }
        return sb.ToString();
    }
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.Spool;
using WinMeetingRecorder.Whisper;

namespace WinMeetingRecorder.GitLab;

/// <summary>
/// GitLab 接続テスト結果
/// </summary>
public record GitLabTestResult(
    bool Success, 
    string Message, 
    string? ProjectName = null, 
    string? DefaultBranch = null, 
    string? WebUrl = null);

/// <summary>
/// 会議セッションのGitLabアップロード結果
/// </summary>
public record GitLabUploadResult(
    bool Success,
    string Message,
    string? CommitId = null,
    string? WebUrl = null,
    int FilesUploaded = 0,
    long TotalBytesUploaded = 0,
    bool SpoolCleaned = false);

/// <summary>
/// 社内GitLab (Self-hosted) REST API 連携サービス
/// </summary>
public class GitLabService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 設定内容に基づいて HttpClient を生成します
    /// </summary>
    private HttpClient CreateHttpClient(GitLabConfig config)
    {
        var handler = new HttpClientHandler();

        if (config.IgnoreSslErrors)
        {
            handler.ServerCertificateCustomValidationCallback = 
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10) // 大容量アップロード対応
        };

        var baseUrl = config.ServerUrl.TrimEnd('/') + "/";
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", config.PersonalAccessToken.Trim());
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    /// <summary>
    /// プロジェクトの疎通テストを実施します (GET /api/v4/projects/:id)
    /// </summary>
    public async Task<GitLabTestResult> TestConnectionAsync(GitLabConfig config)
    {
        if (!config.IsConfigured)
        {
            return new GitLabTestResult(false, "GitLabの接続設定 (URL, プロジェクトID, PAT) が不足しています。");
        }

        try
        {
            using var client = CreateHttpClient(config);
            var escapedProjectId = Uri.EscapeDataString(config.ProjectId.Trim());
            var url = $"api/v4/projects/{escapedProjectId}";

            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var projectName = root.TryGetProperty("name_with_namespace", out var nameProp) 
                    ? nameProp.GetString() 
                    : config.ProjectId;
                var defaultBranch = root.TryGetProperty("default_branch", out var branchProp) 
                    ? branchProp.GetString() 
                    : "main";
                var webUrl = root.TryGetProperty("web_url", out var webUrlProp) 
                    ? webUrlProp.GetString() 
                    : null;

                return new GitLabTestResult(
                    true, 
                    $"接続成功: プロジェクト '{projectName}' にアクセスできました。", 
                    projectName, 
                    defaultBranch, 
                    webUrl);
            }

            // フォールバック: Fine-grained PATで「Project: Read」がない場合、リポジトリAPIを直接検証
            var repoUrl = $"api/v4/projects/{escapedProjectId}/repository/commits?per_page=1";
            var repoResponse = await client.GetAsync(repoUrl);
            if (repoResponse.IsSuccessStatusCode)
            {
                return new GitLabTestResult(
                    true,
                    $"接続成功: リポジトリ '{config.ProjectId}' へのアクセス権限を確認しました。",
                    config.ProjectId,
                    config.Branch,
                    $"{config.ServerUrl.TrimEnd('/')}/{config.ProjectId}");
            }

            return new GitLabTestResult(false, 
                $"GitLab接続エラー (HTTP {(int)response.StatusCode} {response.ReasonPhrase}): {body}");
        }
        catch (Exception ex)
        {
            return new GitLabTestResult(false, $"通信例外: {ex.Message}");
        }
    }

    /// <summary>
    /// 会議セッションを GitLab Commits API を用いて一括アップロードし、成功時にスプールを削除します
    /// </summary>
    public async Task<GitLabUploadResult> UploadSessionAsync(
        string sessionDir, 
        GitLabConfig config, 
        MeetingSessionMetadata? metadata = null,
        bool deleteOnSuccess = true)
    {
        if (!Directory.Exists(sessionDir))
        {
            return new GitLabUploadResult(false, $"指定されたスプールディレクトリが存在しません: {sessionDir}");
        }

        if (!config.IsConfigured)
        {
            return new GitLabUploadResult(false, "GitLabの接続設定が未完了です。");
        }

        var dirInfo = new DirectoryInfo(sessionDir);
        var sessionId = dirInfo.Name;

        // メタデータがなければ復元
        metadata ??= new SpoolService().GetSessionMetadata(sessionDir);

        try
        {
            using var client = CreateHttpClient(config);
            var escapedProjectId = Uri.EscapeDataString(config.ProjectId.Trim());
            var branch = string.IsNullOrWhiteSpace(config.Branch) ? "main" : config.Branch.Trim();
            
            // ベースパスの構築 (例: meetings/20260927_204630)
            var basePath = string.IsNullOrWhiteSpace(config.BasePath) 
                ? sessionId 
                : $"{config.BasePath.Trim().Trim('/')}/{sessionId}";

            var actions = new List<object>();
            long totalBytes = 0;

            // transcript.json の読み込み (存在する場合)
            WhisperTranscriptionResponse? transcriptData = null;
            var transcriptPath = Path.Combine(sessionDir, "transcript.json");
            if (File.Exists(transcriptPath))
            {
                try
                {
                    var transcriptJson = await File.ReadAllTextAsync(transcriptPath);
                    transcriptData = JsonSerializer.Deserialize<WhisperTranscriptionResponse>(transcriptJson, JsonOptions);
                }
                catch { /* ignore */ }
            }

            // 1. README.md の生成＆アクション追加
            var readmeContent = GenerateReadmeMarkdown(sessionId, metadata, basePath, transcriptData);
            var readmeBytes = Encoding.UTF8.GetBytes(readmeContent);
            totalBytes += readmeBytes.Length;
            actions.Add(new
            {
                action = "create",
                file_path = $"{basePath}/README.md",
                content = readmeContent,
                encoding = "text"
            });

            // 2. 音声ファイルの追加 (軽量MP3を最優先、なければWAV)
            var mp3Path = Path.Combine(sessionDir, "meeting_audio.mp3");
            var wavPath = Path.Combine(sessionDir, "meeting_audio.wav");

            if (File.Exists(mp3Path))
            {
                var mp3Bytes = await File.ReadAllBytesAsync(mp3Path);
                totalBytes += mp3Bytes.Length;
                actions.Add(new
                {
                    action = "create",
                    file_path = $"{basePath}/meeting_audio.mp3",
                    content = Convert.ToBase64String(mp3Bytes),
                    encoding = "base64"
                });
                Console.WriteLine($"[GitLabService] 🎵 軽量MP3音声をコミットに追加: {mp3Bytes.Length / 1024.0:F1} KB");
            }
            else if (File.Exists(wavPath))
            {
                var wavBytes = await File.ReadAllBytesAsync(wavPath);
                totalBytes += wavBytes.Length;
                actions.Add(new
                {
                    action = "create",
                    file_path = $"{basePath}/meeting_audio.wav",
                    content = Convert.ToBase64String(wavBytes),
                    encoding = "base64"
                });
                Console.WriteLine($"[GitLabService] 🎙️ WAV音声をコミットに追加: {wavBytes.Length / 1024.0:F1} KB");
            }

            // 3. transcript.json の追加 (文字起こし結果)
            if (File.Exists(transcriptPath))
            {
                var tBytes = await File.ReadAllBytesAsync(transcriptPath);
                totalBytes += tBytes.Length;
                actions.Add(new
                {
                    action = "create",
                    file_path = $"{basePath}/transcript.json",
                    content = Convert.ToBase64String(tBytes),
                    encoding = "base64"
                });
                Console.WriteLine($"[GitLabService] 📝 文字起こし transcript.json をコミットに追加");
            }

            // 4. スクリーンショット画像 (images/*.jpg) の追加
            var imagesDir = Path.Combine(sessionDir, "images");
            int imageCount = 0;
            if (Directory.Exists(imagesDir))
            {
                var imageFiles = Directory.GetFiles(imagesDir, "*.jpg").OrderBy(f => f);
                foreach (var imgPath in imageFiles)
                {
                    var imgBytes = await File.ReadAllBytesAsync(imgPath);
                    totalBytes += imgBytes.Length;
                    var fileName = Path.GetFileName(imgPath);
                    actions.Add(new
                    {
                        action = "create",
                        file_path = $"{basePath}/images/{fileName}",
                        content = Convert.ToBase64String(imgBytes),
                        encoding = "base64"
                    });
                    imageCount++;
                }
            }

            // 5. Commits API ペイロード構築
            string commitTitleSuffix = "";
            if (File.Exists(mp3Path)) commitTitleSuffix += " [MP3]";
            if (transcriptData != null) commitTitleSuffix += " [文字起こし済]";
            var commitMessage = $"docs(meeting): 会議記録 {sessionId} (音声 + スクショ {imageCount}枚{commitTitleSuffix})";
            var payload = new
            {
                branch = branch,
                commit_message = commitMessage,
                actions = actions
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions), 
                Encoding.UTF8, 
                "application/json");

            var url = $"api/v4/projects/{escapedProjectId}/repository/commits";
            Console.WriteLine($"[GitLabService] アップロード開始: {actions.Count} ファイル ({totalBytes / (1024.0 * 1024.0):F2} MB) -> {config.ServerUrl} ({config.ProjectId})");

            var response = await client.PostAsync(url, jsonContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new GitLabUploadResult(
                    false, 
                    $"GitLabコミットAPI失敗 (HTTP {(int)response.StatusCode} {response.ReasonPhrase}): {responseBody}",
                    FilesUploaded: actions.Count,
                    TotalBytesUploaded: totalBytes,
                    SpoolCleaned: false);
            }

            // 成功レスポンスのパース
            string? commitId = null;
            string? commitWebUrl = null;
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idProp)) commitId = idProp.GetString();
                if (root.TryGetProperty("web_url", out var webProp)) commitWebUrl = webProp.GetString();
            }
            catch { /* ignore */ }

            Console.WriteLine($"[GitLabService] ✅ コミット成功: {commitId ?? "OK"}");

            // 5. 送信成功後のスプール自動クリーンアップ
            bool spoolCleaned = false;
            if (deleteOnSuccess)
            {
                try
                {
                    Directory.Delete(sessionDir, true);
                    spoolCleaned = true;
                    Console.WriteLine($"[GitLabService] 🧹 スプールディレクトリを自動クリーンアップしました: {sessionDir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GitLabService] ⚠️ スプール削除警告: {ex.Message}");
                }
            }

            return new GitLabUploadResult(
                true,
                "GitLabへの一括コミットとローカルクリーンアップが正常に完了しました。",
                CommitId: commitId,
                WebUrl: commitWebUrl,
                FilesUploaded: actions.Count,
                TotalBytesUploaded: totalBytes,
                SpoolCleaned: spoolCleaned);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitLabService] ❌ アップロード例外: {ex.Message}");
            return new GitLabUploadResult(
                false, 
                $"アップロード中に例外が発生しました: {ex.Message}",
                SpoolCleaned: false);
        }
    }

    /// <summary>
    /// セッション情報から GitLab 閲覧用 README.md を生成します
    /// 文字起こし結果 (Whisper) がある場合は、発話とスライド画像を同期させた統合タイムラインを出力します
    /// </summary>
    public static string GenerateReadmeMarkdown(
        string sessionId, 
        MeetingSessionMetadata? metadata, 
        string basePath, 
        WhisperTranscriptionResponse? transcript = null)
    {
        var sb = new StringBuilder();
        var titleTime = metadata != null && metadata.StartTime != default 
            ? metadata.StartTime.ToString("yyyy-MM-dd HH:mm:ss") 
            : sessionId;

        sb.AppendLine($"# 🎙️ 会議記録: {titleTime}");
        sb.AppendLine();
        sb.AppendLine($"> この会議記録は **WinMeetingRecorder** により自動記録・生成されました。");
        sb.AppendLine();
        sb.AppendLine("## 📋 会議サマリー");
        sb.AppendLine();
        sb.AppendLine($"- **セッションID**: `{sessionId}`");
        if (metadata != null)
        {
            sb.AppendLine($"- **記録開始**: {metadata.StartTime:yyyy-MM-dd HH:mm:ss}");
            if (metadata.EndTime.HasValue)
            {
                sb.AppendLine($"- **記録終了**: {metadata.EndTime.Value:yyyy-MM-dd HH:mm:ss}");
            }

            var dur = TimeSpan.FromSeconds(metadata.DurationSeconds);
            sb.AppendLine($"- **記録時間**: {(int)dur.TotalMinutes}分 {dur.Seconds:D2}秒 (合計 {metadata.DurationSeconds:F0} 秒)");

            if (!string.IsNullOrEmpty(metadata.MonitorName))
            {
                sb.AppendLine($"- **対象画面**: {metadata.MonitorName}");
            }
        }

        // 音声ファイルリンク
        if (metadata != null && metadata.HasMp3)
        {
            sb.AppendLine($"- **音声ファイル**: [meeting_audio.mp3](./meeting_audio.mp3) 🎵 (24kHz モノラル高圧縮 MP3)");
        }
        else
        {
            sb.AppendLine($"- **音声ファイル**: [meeting_audio.wav](./meeting_audio.wav) (48kHz ステレオ WAV)");
        }

        var imgCount = metadata?.Images.Count ?? 0;
        sb.AppendLine($"- **画面キャプチャ**: {imgCount} 枚");

        if (transcript != null && transcript.Segments.Count > 0)
        {
            sb.AppendLine($"- **文字起こし**: 完了 ({transcript.Segments.Count} 件の発話セグメント, 全 {transcript.Text.Length} 文字) [transcript.json](./transcript.json)");
        }
        sb.AppendLine();

        // 文字起こし全文アコーディオン
        if (transcript != null && !string.IsNullOrWhiteSpace(transcript.Text))
        {
            sb.AppendLine("<details>");
            sb.AppendLine($"<summary>📜 <strong>文字起こし全文テキストを展開 ({transcript.Text.Length} 文字)</strong></summary>");
            sb.AppendLine();
            sb.AppendLine($"> {transcript.Text.Trim()}");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // タイムラインの構築 (文字起こしセグメント ＋ スクリーンショット画像の時系列マージ)
        if (transcript != null && transcript.Segments.Count > 0)
        {
            sb.AppendLine("## 📝 会議タイムライン（発話書き起こし ＆ スライド画像）");
            sb.AppendLine();
            sb.AppendLine("| 経過時間 | 種別 | 発話内容 / スライド画像プレビュー |");
            sb.AppendLine("| :---: | :---: | :--- |");

            var timelineItems = new List<TimelineItem>();

            // 1. スクショ画像の登録
            if (metadata != null)
            {
                foreach (var img in metadata.Images)
                {
                    string reasonText = img.Reason switch
                    {
                        "initial" => "初回キーフレーム",
                        "manual" => "手動撮影 📸",
                        "diff" => $"スライド変化検知 ({img.Diff * 100:F1}%)",
                        "timer" => "定期キーフレーム",
                        _ => img.Reason
                    };

                    var imgRel = $"./images/{img.FileName}";
                    var previewMd = $"[![{reasonText}]({imgRel})]({imgRel})<br>*{reasonText}*";

                    timelineItems.Add(new TimelineItem(
                        SortTime: img.ElapsedSeconds,
                        TimeDisplay: FormatSeconds(img.ElapsedSeconds),
                        Type: "📸 スライド",
                        Content: previewMd));
                }
            }

            // 2. 文字起こしセグメントの登録
            foreach (var seg in transcript.Segments)
            {
                var timeRange = $"{FormatSeconds((int)seg.Start)} - {FormatSeconds((int)seg.End)}";
                timelineItems.Add(new TimelineItem(
                    SortTime: seg.Start,
                    TimeDisplay: timeRange,
                    Type: "🗣️ 発話",
                    Content: EscapeMarkdownTableCell(seg.Text.Trim())));
            }

            // 時系列昇順ソート (同時刻ならスライド画像を先に表示)
            var sorted = timelineItems
                .OrderBy(x => x.SortTime)
                .ThenBy(x => x.Type.StartsWith("📸") ? 0 : 1)
                .ToList();

            foreach (var item in sorted)
            {
                sb.AppendLine($"| `{item.TimeDisplay}` | {item.Type} | {item.Content} |");
            }
        }
        else
        {
            // 文字起こしなし: 従来の画面キャプチャ一覧
            sb.AppendLine("## 📸 タイムライン・画面キャプチャ一覧");
            sb.AppendLine();

        if (metadata == null || metadata.Images.Count == 0)
        {
            sb.AppendLine("*(スクリーンショットは記録されませんでした)*");
        }
        else
        {
            sb.AppendLine("| 経過時間 | 撮影時刻 | 契機 / 差分 | プレビュー (クリックで等倍表示) |");
            sb.AppendLine("| :---: | :---: | :---: | :--- |");

            foreach (var img in metadata.Images)
            {
                var dur = TimeSpan.FromSeconds(img.ElapsedSeconds);
                var timeStr = $"{dur.Hours:D2}:{dur.Minutes:D2}:{dur.Seconds:D2}";
                var timeClock = img.Timestamp != default ? img.Timestamp.ToString("HH:mm:ss") : "-";

                string reasonText = img.Reason switch
                {
                    "initial" => "初回キーフレーム",
                    "manual" => "手動撮影 📸",
                    "diff" => $"スライド変化検知 ({img.Diff * 100:F1}%)",
                    "timer" => "定期キーフレーム",
                    _ => img.Reason
                };

                var imgRel = $"./images/{img.FileName}";
                sb.AppendLine($"| `{timeStr}` | {timeClock} | {reasonText} | [![{timeStr}]({imgRel})]({imgRel}) |");
            }
        }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*WinMeetingRecorder - Windows Dialog-less Meeting Recorder & Change Detector*");

        return sb.ToString();
    }

    private static string FormatSeconds(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static string EscapeMarkdownTableCell(string text)
    {
        return text.Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>");
    }

    private record TimelineItem(double SortTime, string TimeDisplay, string Type, string Content);
}

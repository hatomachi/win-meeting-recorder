using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.Spool;

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

            if (!response.IsSuccessStatusCode)
            {
                return new GitLabTestResult(false, 
                    $"GitLab接続エラー (HTTP {(int)response.StatusCode} {response.ReasonPhrase}): {body}");
            }

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

            // 1. README.md の生成＆アクション追加
            var readmeContent = GenerateReadmeMarkdown(sessionId, metadata, basePath);
            var readmeBytes = Encoding.UTF8.GetBytes(readmeContent);
            totalBytes += readmeBytes.Length;
            actions.Add(new
            {
                action = "create",
                file_path = $"{basePath}/README.md",
                content = readmeContent,
                encoding = "text"
            });

            // 2. 音声ファイル (meeting_audio.wav) の追加
            var audioPath = Path.Combine(sessionDir, "meeting_audio.wav");
            if (File.Exists(audioPath))
            {
                var audioBytes = await File.ReadAllBytesAsync(audioPath);
                totalBytes += audioBytes.Length;
                actions.Add(new
                {
                    action = "create",
                    file_path = $"{basePath}/meeting_audio.wav",
                    content = Convert.ToBase64String(audioBytes),
                    encoding = "base64"
                });
            }

            // 3. スクリーンショット画像 (images/*.jpg) の追加
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

            // 4. Commits API ペイロード構築
            var commitMessage = $"docs(meeting): 会議記録 {sessionId} (音声 + スクショ {imageCount}枚)";
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
    /// </summary>
    public static string GenerateReadmeMarkdown(string sessionId, MeetingSessionMetadata? metadata, string basePath)
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

        sb.AppendLine($"- **音声ファイル**: [meeting_audio.wav](./meeting_audio.wav) (48kHz ステレオ / WASAPI Loopback ＋ マイク合成)");
        var imgCount = metadata?.Images.Count ?? 0;
        sb.AppendLine($"- **画面キャプチャ**: {imgCount} 枚");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
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

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*WinMeetingRecorder - Windows Dialog-less Meeting Recorder & Change Detector*");

        return sb.ToString();
    }
}

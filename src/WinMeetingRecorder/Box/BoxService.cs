using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.Logging;
using WinMeetingRecorder.Spool;

namespace WinMeetingRecorder.Box;

public record BoxUploadResult(bool Success, string Message, string? FolderId = null, string? FolderUrl = null);

public record BoxAuthStatus(
    bool IsConfigured, 
    bool IsAuthenticated, 
    string? UserName, 
    string? UserLogin, 
    DateTime? TokenExpiresAt,
    string? ActiveThemeId,
    string? ActiveThemeName,
    int ThemeCount);

/// <summary>
/// Box API (OAuth 2.0 ユーザー認証, Chunked アップロード, セッション一括保存) サービス
/// </summary>
public class BoxService
{
    private HttpClient? _httpClient;
    private bool? _lastBypassProxy;

    private static HttpClient CreateHttpClient(bool bypassProxy)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
            EnableMultipleHttp2Connections = true
        };

        if (bypassProxy)
        {
            handler.UseProxy = false;
            handler.Proxy = null;
        }
        else
        {
            // Windows OS の標準プロキシ (PAC / WPAD / WinINet) を使用
            handler.UseProxy = true;
            handler.Proxy = HttpClient.DefaultProxy;
            // 会社プロキシの統合Windows認証 (Active Directory / NTLM / Kerberos) にOS資格情報を自動提供
            handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10) // 1時間超の音声MP3や多数スクショの一括アップロードに対応
        };
    }

    private HttpClient Http
    {
        get
        {
            lock (_lock)
            {
                var bypassProxy = _configService.LoadConfig().Box.BypassProxy;
                if (_httpClient == null || _lastBypassProxy != bypassProxy)
                {
                    _httpClient?.Dispose();
                    _httpClient = CreateHttpClient(bypassProxy);
                    _lastBypassProxy = bypassProxy;
                }
                return _httpClient;
            }
        }
    }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private const int CallbackPort = 5001;
    private const string CallbackUrl = "http://localhost:5001/callback/";
    private const string RedirectUri = "http://localhost:5001/callback";

    private readonly ConfigService _configService;
    private readonly object _lock = new();

    public event Action<BoxAuthStatus>? AuthStatusChanged;
    public event Action<List<MeetingTheme>>? ThemesUpdated;
    public event Action<string>? ThemeSelected;

    public BoxService(ConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// 現在の認証状態を取得
    /// </summary>
    public BoxAuthStatus GetAuthStatus()
    {
        var config = _configService.LoadConfig().Box;
        return new BoxAuthStatus(
            config.HasCredentials,
            config.IsAuthenticated,
            config.UserName,
            config.UserLogin,
            config.TokenExpiresAt,
            config.SelectedThemeId,
            config.SelectedThemeName,
            config.Themes.Count);
    }

    /// <summary>
    /// OAuth 2.0 ブラウザログインフローを開始する
    /// 一時HTTPリスナーを起動し、既定ブラウザで認可画面を開いて認可コードを取得する
    /// </summary>
    public async Task<BoxAuthStatus> StartOAuthLoginAsync(CancellationToken cancellationToken = default)
    {
        var appConfig = _configService.LoadConfig();
        var boxConfig = appConfig.Box;

        if (!boxConfig.HasCredentials)
        {
            throw new InvalidOperationException("Box Client ID と Client Secret が設定されていません。設定画面で入力してください。");
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(CallbackUrl);
        
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"認証用コールバックリスナー(ポート{CallbackPort})の起動に失敗しました: {ex.Message}", ex);
        }

        // 認可URLをブラウザでオープン (独自ドメイン・企業SSO対応)
        var authHost = boxConfig.GetAuthHost();
        var subdomain = boxConfig.GetSubdomain();
        var authorizeUrl = $"https://{authHost}/api/oauth2/authorize?response_type=code&client_id={Uri.EscapeDataString(boxConfig.ClientId)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}";
        if (!string.IsNullOrEmpty(subdomain))
        {
            authorizeUrl += $"&box_subdomain={Uri.EscapeDataString(subdomain)}";
        }
        
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authorizeUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BoxService] ブラウザの自動オープンに失敗: {ex.Message} (URL: {authorizeUrl})");
        }

        // リダイレクト待ち (最大120秒)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        try
        {
            var getContextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(getContextTask, Task.Delay(Timeout.Infinite, cts.Token));

            if (completedTask != getContextTask)
            {
                throw new TimeoutException("Box 認証がタイムアウトしました (120秒)");
            }

            var context = await getContextTask;
            var req = context.Request;
            var resp = context.Response;

            var code = req.QueryString["code"];
            var error = req.QueryString["error"];
            var errorDescription = req.QueryString["error_description"];

            // ブラウザへのレスポンスHTML
            string responseHtml;
            if (!string.IsNullOrEmpty(code))
            {
                responseHtml = """
                    <!DOCTYPE html>
                    <html lang="ja">
                    <head><meta charset="utf-8"><title>Box 認証完了</title></head>
                    <body style="font-family:sans-serif;text-align:center;padding:50px;background:#0f172a;color:#f8fafc;">
                        <h1 style="color:#22c55e;">✅ Box 認証に成功しました</h1>
                        <p style="font-size:16px;">WinMeetingRecorder への連携が完了しました。このタブ・ウィンドウを閉じてアプリにお戻りください。</p>
                        <script>setTimeout(function(){ window.close(); }, 3000);</script>
                    </body>
                    </html>
                    """;
            }
            else
            {
                responseHtml = $"""
                    <!DOCTYPE html>
                    <html lang="ja">
                    <head><meta charset="utf-8"><title>Box 認証エラー</title></head>
                    <body style="font-family:sans-serif;text-align:center;padding:50px;background:#0f172a;color:#f8fafc;">
                        <h1 style="color:#ef4444;">❌ Box 認証に失敗しました</h1>
                        <p style="font-size:16px;">{WebUtility.HtmlEncode(error ?? "不明なエラー")}: {WebUtility.HtmlEncode(errorDescription ?? "")}</p>
                    </body>
                    </html>
                    """;
            }

            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            resp.ContentType = "text/html; charset=utf-8";
            resp.ContentLength64 = buffer.Length;
            await resp.OutputStream.WriteAsync(buffer, cts.Token);
            resp.OutputStream.Close();

            if (string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException($"Box 認可エラー: {error} - {errorDescription}");
            }

            // 認可コードをトークンに引き換え
            await ExchangeCodeForTokenAsync(code);

            // 認証ユーザー情報とテーマ一覧を更新
            await RefreshUserInfoAsync();
            await GetThemesAsync(forceRefresh: true);

            var status = GetAuthStatus();
            AuthStatusChanged?.Invoke(status);
            return status;
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    /// <summary>
    /// 認可コードから Access Token / Refresh Token を取得
    /// </summary>
    private async Task ExchangeCodeForTokenAsync(string code)
    {
        var appConfig = _configService.LoadConfig();
        var boxConfig = appConfig.Box;

        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "client_id", boxConfig.ClientId },
            { "client_secret", boxConfig.ClientSecret },
            { "redirect_uri", RedirectUri }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.box.com/oauth2/token")
        {
            Content = new FormUrlEncodedContent(parameters)
        };

        var response = await Http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"トークン取得に失敗しました: {response.StatusCode} - {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        boxConfig.AccessToken = root.GetProperty("access_token").GetString();
        boxConfig.RefreshToken = root.GetProperty("refresh_token").GetString();
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        boxConfig.TokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60);

        _configService.SaveConfig(appConfig);
        Console.WriteLine("[BoxService] 🔑 OAuth2 トークン取得に成功しました。");
    }

    /// <summary>
    /// 有効な Access Token を取得（必要に応じて自動リフレッシュ）
    /// </summary>
    public async Task<string> GetValidAccessTokenAsync()
    {
        var appConfig = _configService.LoadConfig();
        var boxConfig = appConfig.Box;

        // 1. Developer Token が明示されている場合は最優先
        if (!string.IsNullOrWhiteSpace(boxConfig.DeveloperToken))
        {
            return boxConfig.DeveloperToken;
        }

        // 2. 有効期限内の Access Token があればそれを返す
        if (!string.IsNullOrWhiteSpace(boxConfig.AccessToken) &&
            boxConfig.TokenExpiresAt.HasValue &&
            boxConfig.TokenExpiresAt.Value > DateTime.UtcNow)
        {
            return boxConfig.AccessToken;
        }

        // 3. Refresh Token による自動更新
        if (!string.IsNullOrWhiteSpace(boxConfig.RefreshToken) && boxConfig.HasCredentials)
        {
            Console.WriteLine("[BoxService] 🔄 Access Token の有効期限が切れているため、自動リフレッシュを実行します...");
            var parameters = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", boxConfig.RefreshToken },
                { "client_id", boxConfig.ClientId },
                { "client_secret", boxConfig.ClientSecret }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.box.com/oauth2/token")
            {
                Content = new FormUrlEncodedContent(parameters)
            };

            var response = await Http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                boxConfig.AccessToken = root.GetProperty("access_token").GetString();
                boxConfig.RefreshToken = root.GetProperty("refresh_token").GetString();
                var expiresIn = root.GetProperty("expires_in").GetInt32();
                boxConfig.TokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60);

                _configService.SaveConfig(appConfig);
                Console.WriteLine("[BoxService] ✅ トークンの自動リフレッシュに成功しました。");
                return boxConfig.AccessToken!;
            }
            else
            {
                Console.WriteLine($"[BoxService] ⚠️ トークンの自動リフレッシュに失敗しました: {json}");
            }
        }

        if (!string.IsNullOrWhiteSpace(boxConfig.AccessToken))
        {
            return boxConfig.AccessToken;
        }

        throw new InvalidOperationException("Box に未認証です。Web UI またはトレイメニューから Box ログインを行ってください。");
    }

    /// <summary>
    /// 認証ユーザー情報の取得・同期
    /// </summary>
    public async Task<string> RefreshUserInfoAsync()
    {
        var token = await GetValidAccessTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.box.com/2.0/users/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await Http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ユーザー情報の取得に失敗: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var name = doc.RootElement.GetProperty("name").GetString() ?? "";
        var login = doc.RootElement.GetProperty("login").GetString() ?? "";

        var appConfig = _configService.LoadConfig();
        appConfig.Box.UserName = name;
        appConfig.Box.UserLogin = login;
        _configService.SaveConfig(appConfig);

        return $"{name} ({login})";
    }

    /// <summary>
    /// テーマ台帳の取得
    /// ローカルの config.json (exeと同フォルダ) から取得
    /// </summary>
    public Task<List<MeetingTheme>> GetThemesAsync(bool forceRefresh = false)
    {
        var appConfig = _configService.LoadConfig();
        var boxConfig = appConfig.Box;

        var themes = boxConfig.Themes;
        if (themes == null || themes.Count == 0)
        {
            themes = BoxConfig.CreateDefaultThemes(boxConfig.SharedFolderId);
            boxConfig.Themes = themes;
            _configService.SaveConfig(appConfig);
        }

        if (string.IsNullOrEmpty(boxConfig.SelectedThemeId) || !themes.Any(t => t.Id == boxConfig.SelectedThemeId))
        {
            boxConfig.SelectedThemeId = themes[0].Id;
            boxConfig.SelectedThemeName = themes[0].Name;
            _configService.SaveConfig(appConfig);
        }

        if (forceRefresh)
        {
            ThemesUpdated?.Invoke(themes);
            AuthStatusChanged?.Invoke(GetAuthStatus());
            Console.WriteLine($"[BoxService] 📋 テーマ設定をローカルの config.json から再読み込みしました ({themes.Count}件)");
        }

        return Task.FromResult(themes);
    }

    /// <summary>
    /// 会議テーマを選択
    /// </summary>
    public void SelectTheme(string themeId)
    {
        var appConfig = _configService.LoadConfig();
        var theme = appConfig.Box.Themes.FirstOrDefault(t => t.Id == themeId);
        if (theme != null)
        {
            appConfig.Box.SelectedThemeId = theme.Id;
            appConfig.Box.SelectedThemeName = theme.Name;
            _configService.SaveConfig(appConfig);
            AuthStatusChanged?.Invoke(GetAuthStatus());
            ThemeSelected?.Invoke(theme.Id);
            Console.WriteLine($"[BoxService] 会議テーマを切り替えました: {theme.Name} ({theme.Id})");
        }
    }

    /// <summary>
    /// セッションフォルダをBox上に新規作成
    /// </summary>
    public async Task<string> CreateSessionFolderAsync(string parentFolderId, string folderName)
    {
        var token = await GetValidAccessTokenAsync();

        var bodyObj = new
        {
            name = folderName,
            parent = new { id = parentFolderId }
        };

        var jsonBody = JsonSerializer.Serialize(bodyObj);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.box.com/2.0/folders")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await Http.SendAsync(req);
        var jsonRes = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.Created)
        {
            using var doc = JsonDocument.Parse(jsonRes);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        // 既に同名フォルダが存在する場合 (409 Conflict)
        if (res.StatusCode == HttpStatusCode.Conflict)
        {
            using var doc = JsonDocument.Parse(jsonRes);
            if (doc.RootElement.TryGetProperty("context_info", out var ctx) &&
                ctx.TryGetProperty("conflicts", out var conflicts) &&
                conflicts.GetArrayLength() > 0)
            {
                return conflicts[0].GetProperty("id").GetString()!;
            }
        }

        throw new InvalidOperationException($"フォルダ作成に失敗しました: {res.StatusCode} - {jsonRes}");
    }

    /// <summary>
    /// 単一ファイルのアップロード (50MB以下は通常アップロード、50MB超はChunked Upload Session)
    /// </summary>
    public async Task<string> UploadFileAsync(string folderId, string filePath, string? uploadFileName = null, IProgress<long>? progress = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"アップロード対象ファイルが見つかりません: {filePath}");
        }

        var fileInfo = new FileInfo(filePath);
        var fileName = uploadFileName ?? fileInfo.Name;
        const long chunkThreshold = 50L * 1024 * 1024; // 50MB

        if (fileInfo.Length > chunkThreshold)
        {
            Console.WriteLine($"[BoxService] 📦 大容量ファイル ({fileInfo.Length / 1024 / 1024}MB > 50MB) のため Chunked Upload を開始: {fileName}");
            return await UploadLargeFileChunkedAsync(folderId, filePath, fileName, progress);
        }
        else
        {
            return await UploadFileStandardAsync(folderId, filePath, fileName);
        }
    }

    /// <summary>
    /// 通常のマルチパートアップロード (50MB以下)
    /// </summary>
    private async Task<string> UploadFileStandardAsync(string folderId, string filePath, string fileName)
    {
        var token = await GetValidAccessTokenAsync();

        using var form = new MultipartFormDataContent();
        var attrJson = JsonSerializer.Serialize(new
        {
            name = fileName,
            parent = new { id = folderId }
        });

        form.Add(new StringContent(attrJson, Encoding.UTF8, "application/json"), "attributes");

        using var fileStream = File.OpenRead(filePath);
        using var streamContent = new StreamContent(fileStream);
        form.Add(streamContent, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://upload.box.com/api/2.0/files/content")
        {
            Content = form
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await Http.SendAsync(req);
        var jsonRes = await res.Content.ReadAsStringAsync();

        if (res.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(jsonRes);
            var entries = doc.RootElement.GetProperty("entries");
            if (entries.GetArrayLength() > 0)
            {
                return entries[0].GetProperty("id").GetString()!;
            }
        }

        throw new InvalidOperationException($"通常アップロード失敗: {res.StatusCode} - {jsonRes}");
    }

    /// <summary>
    /// Box Chunked Upload API (大容量ファイル用 Upload Session)
    /// </summary>
    private async Task<string> UploadLargeFileChunkedAsync(string folderId, string filePath, string fileName, IProgress<long>? progress)
    {
        var token = await GetValidAccessTokenAsync();
        var fileInfo = new FileInfo(filePath);
        var totalBytes = fileInfo.Length;

        // 1. セッション作成 (POST /files/upload_sessions)
        var createSessionBody = JsonSerializer.Serialize(new
        {
            folder_id = folderId,
            file_size = totalBytes,
            file_name = fileName
        });

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "https://upload.box.com/api/2.0/files/upload_sessions")
        {
            Content = new StringContent(createSessionBody, Encoding.UTF8, "application/json")
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await Http.SendAsync(createReq);
        var createJson = await createRes.Content.ReadAsStringAsync();

        if (!createRes.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Chunked Upload セッション作成失敗: {createRes.StatusCode} - {createJson}");
        }

        using var sessionDoc = JsonDocument.Parse(createJson);
        var sessionRoot = sessionDoc.RootElement;
        var partSize = sessionRoot.GetProperty("part_size").GetInt64();
        var totalParts = sessionRoot.GetProperty("total_parts").GetInt32();
        var endpoints = sessionRoot.GetProperty("session_endpoints");
        var uploadPartUrl = endpoints.GetProperty("upload_part").GetString()!;
        var commitUrl = endpoints.GetProperty("commit").GetString()!;

        // 2. パート分割アップロード
        var committedParts = new List<object>();
        using var fileStream = File.OpenRead(filePath);
        byte[] buffer = new byte[partSize];
        long offset = 0;

        using var sha1Full = SHA1.Create();

        for (int i = 0; i < totalParts; i++)
        {
            int bytesRead = await fileStream.ReadAsync(buffer, 0, (int)Math.Min(partSize, totalBytes - offset));
            if (bytesRead <= 0) break;

            long rangeStart = offset;
            long rangeEnd = offset + bytesRead - 1;

            // パート用 SHA-1 ハッシュ
            using var sha1Part = SHA1.Create();
            var partHash = sha1Part.ComputeHash(buffer, 0, bytesRead);
            var partShaBase64 = Convert.ToBase64String(partHash);

            // 全体ハッシュの途中計算
            sha1Full.TransformBlock(buffer, 0, bytesRead, null, 0);

            using var partContent = new ByteArrayContent(buffer, 0, bytesRead);
            partContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            partContent.Headers.Add("Digest", $"sha={partShaBase64}");
            partContent.Headers.Add("Content-Range", $"bytes {rangeStart}-{rangeEnd}/{totalBytes}");

            using var partReq = new HttpRequestMessage(HttpMethod.Put, uploadPartUrl)
            {
                Content = partContent
            };
            partReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var partRes = await Http.SendAsync(partReq);
            var partJson = await partRes.Content.ReadAsStringAsync();

            if (!partRes.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"パート {i + 1}/{totalParts} アップロード失敗: {partRes.StatusCode} - {partJson}");
            }

            using var partDoc = JsonDocument.Parse(partJson);
            var partObj = partDoc.RootElement.GetProperty("part");
            committedParts.Add(new
            {
                part_id = partObj.GetProperty("part_id").GetString(),
                offset = partObj.GetProperty("offset").GetInt64(),
                size = partObj.GetProperty("size").GetInt64(),
                sha1 = partObj.GetProperty("sha1").GetString()
            });

            offset += bytesRead;
            progress?.Report(offset);
            Console.WriteLine($"[BoxService] ⬆️ Chunked アップロード中: {offset * 100 / totalBytes}% ({i + 1}/{totalParts})");
        }

        sha1Full.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var fullShaBase64 = Convert.ToBase64String(sha1Full.Hash!);

        // 3. セッションのコミット (POST /files/upload_sessions/{id}/commit)
        var commitBody = JsonSerializer.Serialize(new { parts = committedParts });
        using var commitReq = new HttpRequestMessage(HttpMethod.Post, commitUrl)
        {
            Content = new StringContent(commitBody, Encoding.UTF8, "application/json")
        };
        commitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        commitReq.Headers.Add("Digest", $"sha={fullShaBase64}");

        var commitRes = await Http.SendAsync(commitReq);
        var commitJson = await commitRes.Content.ReadAsStringAsync();

        if (commitRes.StatusCode == HttpStatusCode.Created || commitRes.StatusCode == HttpStatusCode.Accepted)
        {
            using var commitDoc = JsonDocument.Parse(commitJson);
            var entries = commitDoc.RootElement.GetProperty("entries");
            if (entries.GetArrayLength() > 0)
            {
                return entries[0].GetProperty("id").GetString()!;
            }
        }

        throw new InvalidOperationException($"Chunked Upload コミット失敗: {commitRes.StatusCode} - {commitJson}");
    }

    /// <summary>
    /// 録音セッションの全ファイルをBoxへ一括自動アップロード
    /// 順序: 1. meeting_audio.mp3 -> 2. screen_*.jpg -> 3. meta.json (★必ず最後)
    /// 成功時はローカルスプール一時ファイルを自動クリーンアップ
    /// </summary>
    public async Task<BoxUploadResult> UploadSessionAsync(
        string sessionDir, 
        MeetingSessionMetadata metadata, 
        string? specifiedThemeId = null,
        Action<int, int, string>? onProgress = null)
    {
        if (!Directory.Exists(sessionDir))
        {
            return new BoxUploadResult(false, $"スプールディレクトリが存在しません: {sessionDir}");
        }

        var appConfig = _configService.LoadConfig();
        var boxConfig = appConfig.Box;

        // テーマの特定
        var themes = await GetThemesAsync();
        var themeId = specifiedThemeId ?? boxConfig.SelectedThemeId;
        var theme = themes.FirstOrDefault(t => t.Id == themeId) ?? themes.FirstOrDefault();

        if (theme == null || string.IsNullOrWhiteSpace(theme.BoxFolderId))
        {
            AppLogger.Error("Boxアップロード失敗: アップロード先となる会議テーマ（BoxフォルダID）が未設定です。");
            return new BoxUploadResult(false, "アップロード先となる会議テーマ（BoxフォルダID）が未設定です。");
        }

        AppLogger.Info($"🚀 Boxへの一括アップロードを開始します (テーマ: {theme.Name}, 親フォルダ: {theme.BoxFolderId})");

        try
        {
            // 1. セッションフォルダをBox上に作成 (例: 20261031_214500_abcd1234)
            var sessionFolderName = metadata.SessionId;
            var boxFolderId = await CreateSessionFolderAsync(theme.BoxFolderId, sessionFolderName);
            AppLogger.Info($"📁 セッションフォルダ作成完了: {sessionFolderName} (Box Folder ID: {boxFolderId})");

            // 2. アップロード対象ファイルのリストアップ
            var uploadFiles = new List<(string LocalPath, string RemoteName)>();

            // 音声ファイル (MP3優先、なければWAV)
            var mp3Path = Path.Combine(sessionDir, "meeting_audio.mp3");
            var wavPath = Path.Combine(sessionDir, "meeting_audio.wav");
            string audioFileName = "meeting_audio.mp3";

            if (File.Exists(mp3Path))
            {
                uploadFiles.Add((mp3Path, "meeting_audio.mp3"));
            }
            else if (File.Exists(wavPath))
            {
                uploadFiles.Add((wavPath, "meeting_audio.wav"));
                audioFileName = "meeting_audio.wav";
            }

            // スクリーンショット画像全件 (screen_*.jpg)
            var imagesDir = Path.Combine(sessionDir, "images");
            var imageItems = new List<BoxMetaImageItem>();

            if (Directory.Exists(imagesDir))
            {
                var jpgFiles = Directory.GetFiles(imagesDir, "*.jpg").OrderBy(f => f).ToList();
                foreach (var imgPath in jpgFiles)
                {
                    var name = Path.GetFileName(imgPath);
                    uploadFiles.Add((imgPath, name));

                    // meta.json 用のタイムスタンプ記録
                    var item = metadata.Images.FirstOrDefault(i => i.FileName == name);
                    var ts = item?.Timestamp ?? File.GetCreationTime(imgPath);
                    imageItems.Add(new BoxMetaImageItem
                    {
                        Filename = name,
                        Timestamp = ts.ToString("yyyy-MM-ddTHH:mm:sszzz")
                    });
                }
            }

            // 3. meta.json の生成 (★アップロードは必ず最後)
            var startTimeStr = metadata.StartTime.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var metaObj = new BoxMetaJson
            {
                SessionId = metadata.SessionId,
                ThemeId = theme.Id,
                Memo = metadata.Memo ?? "",
                StartTime = startTimeStr,
                DurationSec = (int)Math.Round(metadata.DurationSeconds),
                AudioFile = audioFileName,
                ImageCount = imageItems.Count,
                ImageFiles = imageItems
            };

            var metaJsonPath = Path.Combine(sessionDir, "meta.json");
            var metaJsonString = JsonSerializer.Serialize(metaObj, JsonOptions);
            await File.WriteAllTextAsync(metaJsonPath, metaJsonString, Encoding.UTF8);

            // 合計ファイル数: 音声 + スクショ + meta.json
            int totalFiles = uploadFiles.Count + 1;
            int current = 0;

            // 4. 音声と画像のアップロード
            foreach (var (localPath, remoteName) in uploadFiles)
            {
                current++;
                onProgress?.Invoke(current, totalFiles, remoteName);
                Console.WriteLine($"[BoxService] ⬆️ アップロード中 ({current}/{totalFiles}): {remoteName}");
                await UploadFileAsync(boxFolderId, localPath, remoteName);
            }

            // 5. ★一番最後に meta.json をアップロード (後段サーバーの完了検知トリガー)
            current++;
            onProgress?.Invoke(current, totalFiles, "meta.json");
            AppLogger.Info($"⭐ [最終トリガー] meta.json をアップロード中 ({current}/{totalFiles})...");
            await UploadFileAsync(boxFolderId, metaJsonPath, "meta.json");

            // 6. 二重安全確認: Box API でフォルダ内の全ファイルを再取得し、実在・サイズを厳格に検証
            AppLogger.Info($"🔍 [二重安全確認] Box APIで全ファイルの実在を検証中 (Folder ID: {boxFolderId})...");
            var expectedNames = uploadFiles.Select(f => f.RemoteName).Append("meta.json").ToList();
            await VerifyFolderFilesAsync(boxFolderId, expectedNames);

            var folderUrl = boxConfig.GetFolderUrl(boxFolderId);
            var startTimeDisplay = metadata.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var memoDisplay = !string.IsNullOrWhiteSpace(metadata.Memo) ? $" [メモ: {metadata.Memo}]" : "";

            // ★目立つログ出力（日時・テーマ・ファイル数・Boxリンク）
            AppLogger.Info($"✅ 【Box格納完了】テーマ: {theme.Name}{memoDisplay} ｜ {startTimeDisplay} 会議 (計{totalFiles}ファイル)");
            AppLogger.Info($"🔗 Boxフォルダ: {folderUrl}");

            // 7. 実在確認完了後のみ、ローカルスプール一時ファイルを自動クリーンアップ
            TryCleanupSpoolFiles(sessionDir);
            AppLogger.Info($"🧹 ローカルスプール一時ファイルを安全に消去しました: {sessionFolderName}");

            return new BoxUploadResult(true, "Box への全件アップロードおよび実在検証が完了しました。", boxFolderId, folderUrl);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"❌ Box アップロード失敗 (スプールは安全に保持されます): {ex.Message}");
            return new BoxUploadResult(false, $"Box アップロード失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Box API (GET /folders/{folderId}/items) を呼び出し、
    /// アップロードした全ファイルがBox上に確実に存在し、ファイルサイズが0より大きいことを厳格に検証する
    /// </summary>
    private async Task VerifyFolderFilesAsync(string boxFolderId, List<string> expectedFileNames)
    {
        var token = await GetValidAccessTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.box.com/2.0/folders/{boxFolderId}/items?fields=id,name,type,size&limit=1000");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await Http.SendAsync(req);
        var jsonRes = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Box上のファイル実在確認APIに失敗しました: {res.StatusCode} - {jsonRes}");
        }

        using var doc = JsonDocument.Parse(jsonRes);
        var entries = doc.RootElement.GetProperty("entries");

        var boxFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.GetProperty("type").GetString() == "file")
            {
                var name = entry.GetProperty("name").GetString()!;
                var size = entry.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                boxFiles[name] = size;
            }
        }

        var missingFiles = new List<string>();
        var zeroByteFiles = new List<string>();

        foreach (var expected in expectedFileNames)
        {
            if (!boxFiles.TryGetValue(expected, out var size))
            {
                missingFiles.Add(expected);
            }
            else if (size <= 0)
            {
                zeroByteFiles.Add(expected);
            }
        }

        if (missingFiles.Count > 0 || zeroByteFiles.Count > 0)
        {
            var msg = new StringBuilder("Box上のファイル検証に失敗しました:");
            if (missingFiles.Count > 0)
            {
                msg.Append($" [未検出: {string.Join(", ", missingFiles)}]");
            }
            if (zeroByteFiles.Count > 0)
            {
                msg.Append($" [0バイト不正: {string.Join(", ", zeroByteFiles)}]");
            }
            throw new InvalidOperationException(msg.ToString());
        }
    }

    /// <summary>
    /// アップロード成功後のスプール一時ファイルクリーンアップ
    /// (WAV, MP3, images/*.jpg, meta.json を消去。セッション情報 metadata.json は削除または保管)
    /// </summary>
    private static void TryCleanupSpoolFiles(string sessionDir)
    {
        try
        {
            // ディレクトリごと削除
            if (Directory.Exists(sessionDir))
            {
                Directory.Delete(sessionDir, true);
                Console.WriteLine($"[BoxService] 🧹 スプールディレクトリを自動クリーンアップしました: {sessionDir}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BoxService] ⚠️ クリーンアップ警告: {ex.Message}");
        }
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.Logging;
using WinMeetingRecorder.Spool;

namespace WinMeetingRecorder.Box;

public record BoxUploadResult(
    bool Success,
    string Message,
    string? FolderId = null,
    string? FolderUrl = null,
    string? FileId = null,
    string? FileName = null);

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
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

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

    private class BoxUnauthorizedException : Exception
    {
        public BoxUnauthorizedException(string message) : base(message) { }
    }

    /// <summary>
    /// トークンを取得してアクションを実行し、401 Unauthorized 発生時はトークンを強制リフレッシュして1回だけ自動リトライする
    /// </summary>
    private async Task<T> ExecuteWithTokenRetryAsync<T>(Func<string, Task<T>> action)
    {
        var token = await GetValidAccessTokenAsync();
        try
        {
            return await action(token);
        }
        catch (BoxUnauthorizedException ex)
        {
            AppLogger.Warn($"[BoxService] ⚠️ 401 Unauthorized を検知しました。トークンを強制リフレッシュして1回リトライします: {ex.Message}");
            token = await GetValidAccessTokenAsync(forceRefresh: true);
            return await action(token);
        }
    }

    /// <summary>
    /// 有効な Access Token を取得（必要に応じて自動リフレッシュ）
    /// forceRefresh = true の場合は有効期限にかかわらず強制リフレッシュを実行する
    /// </summary>
    public async Task<string> GetValidAccessTokenAsync(bool forceRefresh = false)
    {
        var appConfig = _configService.LoadConfig();
        var boxConfig = appConfig.Box;

        // 1. Developer Token が明示されている場合は最優先
        if (!string.IsNullOrWhiteSpace(boxConfig.DeveloperToken))
        {
            return boxConfig.DeveloperToken;
        }

        // 2. 強制更新でなく、安全マージン(2分)以上の有効な Access Token があればそれを返す
        if (!forceRefresh && boxConfig.IsAccessTokenValid)
        {
            return boxConfig.AccessToken!;
        }

        // 3. Refresh Token による自動更新（SemaphoreSlim による排他制御 & ダブルチェック）
        if (boxConfig.CanRefreshToken)
        {
            await _tokenLock.WaitAsync();
            try
            {
                // ロック待機中に別スレッドがリフレッシュを完了していないか再確認
                appConfig = _configService.LoadConfig();
                boxConfig = appConfig.Box;

                if (!forceRefresh && boxConfig.IsAccessTokenValid)
                {
                    return boxConfig.AccessToken!;
                }

                if (!boxConfig.CanRefreshToken)
                {
                    throw new InvalidOperationException("Box Refresh Token またはクライアント設定が失われました。再ログインしてください。");
                }

                AppLogger.Info("[BoxService] 🔄 Access Token の有効期限が切れている（または残り2分未満）ため、自動リフレッシュを実行します...");

                var parameters = new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", boxConfig.RefreshToken! },
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
                    AppLogger.Info($"[BoxService] ✅ トークンの自動リフレッシュに成功しました (次回失効予定: {boxConfig.TokenExpiresAt:yyyy-MM-dd HH:mm:ss} UTC)");

                    AuthStatusChanged?.Invoke(GetAuthStatus());
                    return boxConfig.AccessToken!;
                }
                else
                {
                    var errMsg = $"[BoxService] ⚠️ トークンの自動リフレッシュに失敗しました: {response.StatusCode} - {json}";
                    AppLogger.Error(errMsg);
                    throw new InvalidOperationException($"Box トークンの自動更新に失敗しました ({response.StatusCode})。Web UI から再ログインを行ってください。");
                }
            }
            finally
            {
                _tokenLock.Release();
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
        return await ExecuteWithTokenRetryAsync(async token =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.box.com/2.0/users/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await Http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new BoxUnauthorizedException($"ユーザー情報取得認証エラー (401 Unauthorized): {json}");
            }

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
        });
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
            return await UploadFileStandardAsync(folderId, filePath, fileName, progress);
        }
    }

    /// <summary>
    /// 通常のマルチパートアップロード (50MB以下 / 同名衝突時は新バージョンとして上書き)
    /// </summary>
    private async Task<string> UploadFileStandardAsync(string folderId, string filePath, string fileName, IProgress<long>? progress)
    {
        return await ExecuteWithTokenRetryAsync(async token =>
        {
            var createAttrJson = JsonSerializer.Serialize(new
            {
                name = fileName,
                parent = new { id = folderId }
            });

            var (res, jsonRes) = await SendMultipartUploadAsync(
                "https://upload.box.com/api/2.0/files/content",
                createAttrJson,
                filePath,
                fileName,
                token,
                progress);

            if (res.StatusCode == HttpStatusCode.Conflict)
            {
                var existingFileId = TryExtractConflictFileId(jsonRes);
                if (!string.IsNullOrEmpty(existingFileId))
                {
                    AppLogger.Info($"[BoxService] ℹ️ 同名ZIPがBox上に既に存在するため新バージョンとして上書きします: {fileName} (File ID: {existingFileId})");
                    var updateAttrJson = JsonSerializer.Serialize(new { name = fileName });
                    (res, jsonRes) = await SendMultipartUploadAsync(
                        $"https://upload.box.com/api/2.0/files/{existingFileId}/content",
                        updateAttrJson,
                        filePath,
                        fileName,
                        token,
                        progress);
                }
            }

            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(jsonRes);
                var entries = doc.RootElement.GetProperty("entries");
                if (entries.GetArrayLength() > 0)
                {
                    return entries[0].GetProperty("id").GetString()!;
                }
            }

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new BoxUnauthorizedException($"通常アップロード認証エラー (401 Unauthorized): {jsonRes}");
            }

            throw new InvalidOperationException($"通常アップロード失敗: {res.StatusCode} - {jsonRes}");
        });
    }

    private async Task<(HttpResponseMessage Response, string Body)> SendMultipartUploadAsync(
        string url,
        string attrJson,
        string filePath,
        string fileName,
        string token,
        IProgress<long>? progress)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(attrJson, Encoding.UTF8, "application/json"), "attributes");

        var fileLength = new FileInfo(filePath).Length;
        using Stream uploadStream = progress != null
            ? new ProgressStream(File.OpenRead(filePath), progress, fileLength)
            : File.OpenRead(filePath);
        using var streamContent = new StreamContent(uploadStream);
        form.Add(streamContent, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = form
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await Http.SendAsync(req);
        var jsonRes = await res.Content.ReadAsStringAsync();
        return (res, jsonRes);
    }

    /// <summary>
    /// Box Chunked Upload API (大容量ファイル用 Upload Session / 同名衝突時は新バージョンとして上書き)
    /// </summary>
    private async Task<string> UploadLargeFileChunkedAsync(string folderId, string filePath, string fileName, IProgress<long>? progress)
    {
        return await ExecuteWithTokenRetryAsync(async token =>
        {
            var fileInfo = new FileInfo(filePath);
            var totalBytes = fileInfo.Length;

            // 1. セッション作成 (POST /files/upload_sessions)
            var (createRes, createJson) = await CreateUploadSessionRequestAsync(
                "https://upload.box.com/api/2.0/files/upload_sessions",
                JsonSerializer.Serialize(new
                {
                    folder_id = folderId,
                    file_size = totalBytes,
                    file_name = fileName
                }),
                token);

            if (createRes.StatusCode == HttpStatusCode.Conflict)
            {
                var existingFileId = TryExtractConflictFileId(createJson);
                if (!string.IsNullOrEmpty(existingFileId))
                {
                    AppLogger.Info($"[BoxService] ℹ️ 同名大容量ZIPがBox上に既に存在するため新バージョンとして上書きします: {fileName} (File ID: {existingFileId})");
                    (createRes, createJson) = await CreateUploadSessionRequestAsync(
                        $"https://upload.box.com/api/2.0/files/{existingFileId}/upload_sessions",
                        JsonSerializer.Serialize(new
                        {
                            file_size = totalBytes,
                            file_name = fileName
                        }),
                        token);
                }
            }

            if (createRes.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new BoxUnauthorizedException($"Chunked Upload セッション作成時の認証エラー (401 Unauthorized): {createJson}");
            }

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
            var abortUrl = endpoints.TryGetProperty("abort", out var abortProp) ? abortProp.GetString() : null;

            try
            {
                // 2. パート分割アップロード
                var committedParts = new List<object>();
                using var fileStream = File.OpenRead(filePath);
                byte[] buffer = new byte[partSize];
                long offset = 0;

                using var sha1Full = SHA1.Create();

                for (int i = 0; i < totalParts; i++)
                {
                    int targetBytes = (int)Math.Min(partSize, totalBytes - offset);
                    int bytesRead = 0;
                    while (bytesRead < targetBytes)
                    {
                        int n = await fileStream.ReadAsync(buffer, bytesRead, targetBytes - bytesRead);
                        if (n <= 0) break;
                        bytesRead += n;
                    }
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

                    if (partRes.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new BoxUnauthorizedException($"パート {i + 1}/{totalParts} 認証エラー (401 Unauthorized): {partJson}");
                    }

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

                if (commitRes.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new BoxUnauthorizedException($"Chunked Upload コミット認証エラー (401 Unauthorized): {commitJson}");
                }

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
            catch
            {
                if (!string.IsNullOrEmpty(abortUrl))
                {
                    await TryAbortUploadSessionAsync(abortUrl, token);
                }
                throw;
            }
        });
    }

    private async Task<(HttpResponseMessage Response, string Body)> CreateUploadSessionRequestAsync(
        string url,
        string bodyJson,
        string token)
    {
        using var createReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await Http.SendAsync(createReq);
        var createJson = await createRes.Content.ReadAsStringAsync();
        return (createRes, createJson);
    }

    private async Task TryAbortUploadSessionAsync(string abortUrl, string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, abortUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await Http.SendAsync(req);
        }
        catch { /* ignore cleanup failure */ }
    }

    private static string? TryExtractConflictFileId(string jsonRes)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonRes);
            if (doc.RootElement.TryGetProperty("context_info", out var ctx) &&
                ctx.TryGetProperty("conflicts", out var conflicts))
            {
                if (conflicts.ValueKind == JsonValueKind.Object &&
                    conflicts.TryGetProperty("id", out var idProp))
                {
                    return idProp.GetString();
                }

                if (conflicts.ValueKind == JsonValueKind.Array &&
                    conflicts.GetArrayLength() > 0 &&
                    conflicts[0].TryGetProperty("id", out var arrIdProp))
                {
                    return arrIdProp.GetString();
                }
            }
        }
        catch { /* ignore parse errors */ }

        return null;
    }

    /// <summary>
    /// 録音セッションを &lt;SessionId&gt;.zip に圧縮し、テーマ親フォルダ直下へ1ファイルとしてアップロード
    /// ZIP直下構造: meeting_audio.mp3, images/*.jpg, meta.json (余計な親フォルダ階層なし)
    /// 成功時はローカルのZIPファイルおよびスプール一時ファイルを自動クリーンアップ
    /// </summary>
    public async Task<BoxUploadResult> UploadSessionAsync(
        string sessionDir, 
        MeetingSessionMetadata metadata, 
        string? specifiedThemeId = null,
        Action<int, string, string>? onProgress = null)
    {
        if (!Directory.Exists(sessionDir))
        {
            return new BoxUploadResult(false, $"スプールディレクトリが存在しません: {sessionDir}");
        }

        var appConfig = _configService.LoadConfig();
        var boxConfig = appConfig.Box;

        // テーマの特定 (明示指定 > メタデータ永続化テーマ > 設定ファイルの既定選択)
        var themes = await GetThemesAsync();
        var themeId = specifiedThemeId ?? metadata?.ThemeId ?? boxConfig.SelectedThemeId;
        var theme = themes.FirstOrDefault(t => t.Id == themeId) ?? themes.FirstOrDefault();

        if (theme == null || string.IsNullOrWhiteSpace(theme.BoxFolderId))
        {
            AppLogger.Error("Boxアップロード失敗: アップロード先となる会議テーマ（BoxフォルダID）が未設定です。");
            return new BoxUploadResult(false, "アップロード先となる会議テーマ（BoxフォルダID）が未設定です。");
        }

        // 0. トークンの事前検証・自動リフレッシュ（60分経過後の期限切れトークンを事前に新調）
        try
        {
            await GetValidAccessTokenAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Boxアップロード失敗: 認証トークンの準備に失敗しました (再ログインが必要です): {ex.Message}");
            return new BoxUploadResult(false, $"Box 認証トークンの準備に失敗しました: {ex.Message}");
        }

        var trimmedSessionDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDir));
        var sessionId = !string.IsNullOrWhiteSpace(metadata.SessionId)
            ? metadata.SessionId.Trim()
            : new DirectoryInfo(trimmedSessionDir).Name;
        var zipFileName = $"{sessionId}.zip";
        var zipParentDir = Directory.GetParent(trimmedSessionDir)?.FullName ?? Path.GetTempPath();
        var zipPath = Path.Combine(zipParentDir, zipFileName);

        AppLogger.Info($"🚀 BoxへのZIPアップロードを開始します (テーマ: {theme.Name}, 親フォルダ: {theme.BoxFolderId}, ファイル: {zipFileName})");

        try
        {
            // 1. パッケージ対象ファイルの収集 (MP3優先・なければWAV、および images/*.jpg)
            var mp3Path = Path.Combine(trimmedSessionDir, "meeting_audio.mp3");
            var wavPath = Path.Combine(trimmedSessionDir, "meeting_audio.wav");
            string? audioLocalPath = null;
            string audioFileName = "meeting_audio.mp3";

            if (File.Exists(mp3Path))
            {
                audioLocalPath = mp3Path;
                audioFileName = "meeting_audio.mp3";
            }
            else if (File.Exists(wavPath))
            {
                audioLocalPath = wavPath;
                audioFileName = "meeting_audio.wav";
            }

            var imagesDir = Path.Combine(trimmedSessionDir, "images");
            var jpgFiles = Directory.Exists(imagesDir)
                ? Directory.GetFiles(imagesDir, "*.jpg").OrderBy(f => f).ToList()
                : new List<string>();

            var imageItems = new List<BoxMetaImageItem>();
            foreach (var imgPath in jpgFiles)
            {
                var name = Path.GetFileName(imgPath);
                var item = metadata.Images.FirstOrDefault(i => i.FileName == name);
                var ts = item?.Timestamp ?? File.GetCreationTime(imgPath);
                imageItems.Add(new BoxMetaImageItem
                {
                    Filename = name,
                    Timestamp = ts.ToString("yyyy-MM-ddTHH:mm:sszzz")
                });
            }

            // 2. meta.json の生成
            var startTimeStr = metadata.StartTime.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var metaObj = new BoxMetaJson
            {
                SessionId = sessionId,
                ThemeId = theme.Id,
                Memo = metadata.Memo ?? "",
                StartTime = startTimeStr,
                DurationSec = (int)Math.Round(metadata.DurationSeconds),
                AudioFile = audioFileName,
                ImageCount = imageItems.Count,
                ImageFiles = imageItems
            };

            var metaJsonPath = Path.Combine(trimmedSessionDir, "meta.json");
            var metaJsonString = JsonSerializer.Serialize(metaObj, JsonOptions);
            await File.WriteAllTextAsync(metaJsonPath, metaJsonString, Encoding.UTF8);

            // 3. セッションまるごと <SessionId>.zip に圧縮 (zip直下に mp3, images/, meta.json)
            onProgress?.Invoke(10, $"セッションをZIP圧縮中: {zipFileName}", zipFileName);
            AppLogger.Info($"🗜️ セッションをZIP圧縮中: {zipFileName} (音声: {(audioLocalPath != null ? audioFileName : "なし")}, 画像: {jpgFiles.Count}枚)");

            await CreateSessionZipAsync(zipPath, audioLocalPath, audioFileName, jpgFiles, metaJsonPath);

            var zipSizeBytes = new FileInfo(zipPath).Length;
            var zipSizeMb = zipSizeBytes / 1024.0 / 1024.0;
            AppLogger.Info($"📦 ZIP圧縮完了: {zipFileName} ({zipSizeMb:F2} MB) -> 親フォルダ {theme.BoxFolderId} 直下へアップロード開始");

            // 4. 親フォルダ直下へ <SessionId>.zip を1ファイルでアップロード (サブフォルダ作成なし)
            var uploadProgress = new SyncProgress<long>(bytesSent =>
            {
                if (zipSizeBytes <= 0) return;
                int uploadPct = Math.Clamp((int)(bytesSent * 100 / zipSizeBytes), 0, 100);
                int overallPct = 20 + (int)(uploadPct * 0.70);
                onProgress?.Invoke(overallPct, $"BoxへZIPアップロード中: {zipFileName} ({uploadPct}%)", $"{zipFileName} ({uploadPct}%)");
            });

            onProgress?.Invoke(20, $"BoxへZIPアップロード中: {zipFileName} (0%)", $"{zipFileName} (0%)");
            var uploadedFileId = await UploadFileAsync(theme.BoxFolderId, zipPath, zipFileName, uploadProgress);

            // 5. 二重安全確認: Box API でアップロード済みZIPファイルの実在・親フォルダ・サイズを厳格検証
            onProgress?.Invoke(92, $"🔍 Box上のZIPファイル実在・サイズを確認中: {zipFileName}", "検証中...");
            AppLogger.Info($"🔍 [二重安全確認] Box APIでZIPファイルの実在・サイズを検証中 (File ID: {uploadedFileId})...");
            await VerifyUploadedFileAsync(uploadedFileId, zipFileName, theme.BoxFolderId, zipSizeBytes);

            var folderUrl = boxConfig.GetFolderUrl(theme.BoxFolderId);
            var startTimeDisplay = metadata.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var memoDisplay = !string.IsNullOrWhiteSpace(metadata.Memo) ? $" [メモ: {metadata.Memo}]" : "";

            // ★目立つログ出力（日時・テーマ・ZIPファイル情報・Boxリンク）
            AppLogger.Info($"✅ 【Box格納完了】テーマ: {theme.Name}{memoDisplay} ｜ {startTimeDisplay} 会議 ({zipFileName}, {zipSizeMb:F2} MB, 画像{imageItems.Count}枚)");
            AppLogger.Info($"🔗 Boxフォルダ: {folderUrl}");

            // 6. 実在確認完了後のみ、ローカルのZIPファイルおよびスプール一時ファイルを自動クリーンアップ
            TryDeleteFile(zipPath);
            TryCleanupSpoolFiles(trimmedSessionDir);
            AppLogger.Info($"🧹 ローカルZIPおよびスプール一時ファイルを安全に消去しました: {zipFileName}");

            return new BoxUploadResult(
                true,
                $"Box へのZIPアップロード ({zipFileName}) および実在検証が完了しました。",
                theme.BoxFolderId,
                folderUrl,
                uploadedFileId,
                zipFileName);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"❌ Box アップロード失敗 (スプールは安全に保持されます): {ex.Message}");
            return new BoxUploadResult(false, $"Box アップロード失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// ZIP直下に mp3, images/, meta.json を配置するアーカイブを作成（余計な親フォルダ階層なし）
    /// </summary>
    private static async Task CreateSessionZipAsync(
        string zipPath,
        string? audioLocalPath,
        string audioFileName,
        List<string> jpgFiles,
        string metaJsonPath)
    {
        var tempZipPath = zipPath + ".tmp";
        await Task.Run(() =>
        {
            try
            {
                TryDeleteFile(tempZipPath);

                using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8))
                {
                    if (audioLocalPath != null)
                    {
                        archive.CreateEntryFromFile(audioLocalPath, audioFileName, CompressionLevel.Fastest);
                    }

                    if (jpgFiles.Count > 0)
                    {
                        foreach (var imgPath in jpgFiles)
                        {
                            var name = Path.GetFileName(imgPath);
                            archive.CreateEntryFromFile(imgPath, $"images/{name}", CompressionLevel.Fastest);
                        }
                    }
                    else
                    {
                        // 画像0枚の場合でもzip直下に images/ フォルダが存在するようにする
                        archive.CreateEntry("images/");
                    }

                    archive.CreateEntryFromFile(metaJsonPath, "meta.json", CompressionLevel.Fastest);
                }

                File.Move(tempZipPath, zipPath, overwrite: true);
            }
            catch
            {
                TryDeleteFile(tempZipPath);
                throw;
            }
        });
    }

    /// <summary>
    /// Box API (GET /files/{fileId}) を呼び出し、
    /// アップロードしたZIPファイルが親フォルダ直下に確実に存在し、ファイルサイズが一致することを厳格に検証する
    /// </summary>
    private async Task VerifyUploadedFileAsync(
        string fileId,
        string expectedFileName,
        string expectedParentFolderId,
        long expectedSize)
    {
        await ExecuteWithTokenRetryAsync(async token =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.box.com/2.0/files/{fileId}?fields=id,name,size,parent");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await Http.SendAsync(req);
            var jsonRes = await res.Content.ReadAsStringAsync();

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new BoxUnauthorizedException($"Boxファイル実在確認APIの認証エラー (401 Unauthorized): {jsonRes}");
            }

            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Box上のファイル実在確認APIに失敗しました: {res.StatusCode} - {jsonRes}");
            }

            using var doc = JsonDocument.Parse(jsonRes);
            var root = doc.RootElement;
            var actualName = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var actualSize = root.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            var actualParentId = root.TryGetProperty("parent", out var p) && p.TryGetProperty("id", out var pid)
                ? pid.GetString()
                : null;

            if (!string.Equals(actualName, expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Box上のファイル名が一致しません (期待: {expectedFileName}, 実際: {actualName})");
            }

            if (!string.Equals(actualParentId, expectedParentFolderId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Box上の親フォルダIDが一致しません (期待: {expectedParentFolderId}, 実際: {actualParentId})");
            }

            if (actualSize <= 0 || actualSize != expectedSize)
            {
                throw new InvalidOperationException($"Box上のファイルサイズが一致しません (期待: {expectedSize} bytes, 実際: {actualSize} bytes)");
            }

            return true;
        });
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BoxService] ⚠️ ファイル削除警告 ({filePath}): {ex.Message}");
        }
    }

    /// <summary>
    /// アップロード成功後のスプール一時ファイルクリーンアップ
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

    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly IProgress<long> _progress;
        private readonly long _totalLength;
        private long _lastReportedBytes;

        public ProgressStream(Stream inner, IProgress<long> progress, long totalLength)
        {
            _inner = inner;
            _progress = progress;
            _totalLength = totalLength;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            ReportIfNeeded(n);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int n = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            ReportIfNeeded(n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = await _inner.ReadAsync(buffer, cancellationToken);
            ReportIfNeeded(n);
            return n;
        }

        private void ReportIfNeeded(int bytesRead)
        {
            if (bytesRead <= 0) return;
            long pos = Math.Min(_totalLength, _inner.Position);
            if (pos >= _totalLength || pos - _lastReportedBytes >= 262144)
            {
                _lastReportedBytes = pos;
                _progress.Report(pos);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SyncProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value) => _handler(value);
    }
}

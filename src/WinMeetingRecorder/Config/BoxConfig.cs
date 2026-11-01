using WinMeetingRecorder.Box;

namespace WinMeetingRecorder.Config;

/// <summary>
/// Box API 連携設定 (OAuth 2.0 ユーザー認証 & アップロード先管理)
/// </summary>
public class BoxConfig
{
    /// <summary>
    /// Box OAuth 2.0 クライアントID
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Box OAuth 2.0 クライアントシークレット
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// 開発用 Developer Token (手動設定時・有効期限1時間)
    /// </summary>
    public string? DeveloperToken { get; set; }

    /// <summary>
    /// OAuth 2.0 Access Token
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// OAuth 2.0 Refresh Token
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// トークンの失効予定日時 (UTC)
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// 認証済みユーザーの表示名 (例: "S I")
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 認証済みユーザーのメールアドレス (例: "user@example.com")
    /// </summary>
    public string? UserLogin { get; set; }

    /// <summary>
    /// themes.json が格納されている共通フォルダID (またはルートフォルダID)
    /// </summary>
    public string SharedFolderId { get; set; } = "433029580764";

    /// <summary>
    /// 会社のカスタムBoxドメインまたはサブドメイン (例: "mycompany", "mycompany.app.box.com", "mycompany.box.com")
    /// 空の場合は標準の Box ドメイン ("account.box.com" / "app.box.com") を使用
    /// </summary>
    public string Domain { get; set; } = "";

    /// <summary>
    /// 現在選択されている会議テーマID
    /// </summary>
    public string SelectedThemeId { get; set; } = "";

    /// <summary>
    /// 現在選択されている会議テーマ表示名
    /// </summary>
    public string SelectedThemeName { get; set; } = "";

    /// <summary>
    /// themes.json から読み込んだテーマ台帳キャッシュ (オフライン時フォールバック用)
    /// </summary>
    public List<MeetingTheme> ThemesCache { get; set; } = new();

    /// <summary>
    /// 録音停止時にBoxへ自動アップロード＆スプール消去するか (デフォルト: true)
    /// </summary>
    public bool AutoUploadOnStop { get; set; } = true;

    /// <summary>
    /// クライアント認証情報が入力されているか
    /// </summary>
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>
    /// 有効なトークンまたはDeveloperTokenを保持しているか
    /// </summary>
    public bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(DeveloperToken) ||
        (!string.IsNullOrWhiteSpace(AccessToken) && (!TokenExpiresAt.HasValue || TokenExpiresAt.Value > DateTime.UtcNow));

    /// <summary>
    /// 抽出された企業サブドメイン (例: "mycompany")
    /// </summary>
    public string? GetSubdomain()
    {
        if (string.IsNullOrWhiteSpace(Domain)) return null;
        var clean = Domain.Trim();
        if (clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(8);
        else if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(7);
        clean = clean.TrimEnd('/');

        // 例: "mycompany.app.box.com", "mycompany.account.box.com", "mycompany.box.com"
        var parts = clean.Split('.');
        if (parts.Length > 1)
        {
            return parts[0];
        }
        return clean;
    }

    /// <summary>
    /// OAuth 認可画面のホスト名 (例: "mycompany.account.box.com" または "account.box.com")
    /// </summary>
    public string GetAuthHost()
    {
        var sub = GetSubdomain();
        return string.IsNullOrEmpty(sub) ? "account.box.com" : $"{sub}.account.box.com";
    }

    /// <summary>
    /// Box フォルダ閲覧URL (例: "https://mycompany.app.box.com/folder/{id}" または "https://app.box.com/folder/{id}")
    /// </summary>
    public string GetFolderUrl(string folderId)
    {
        var sub = GetSubdomain();
        var host = string.IsNullOrEmpty(sub) ? "app.box.com" : $"{sub}.app.box.com";
        return $"https://{host}/folder/{folderId}";
    }
}

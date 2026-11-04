using System.Text.Json.Serialization;
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
    /// 既定のBox親フォルダID (テーマ未指定時のフォールバック用)
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
    public string SelectedThemeId { get; set; } = "employee_only";

    /// <summary>
    /// 現在選択されている会議テーマ表示名
    /// </summary>
    public string SelectedThemeName { get; set; } = "社員限定";

    /// <summary>
    /// 定義されている会議テーマ一覧 (ローカルの config.json から取得)
    /// </summary>
    public List<MeetingTheme> Themes { get; set; } = CreateDefaultThemes();

    /// <summary>
    /// 旧バージョン互換用プロパティ (ThemesCache -> Themes)
    /// </summary>
    [JsonIgnore]
    public List<MeetingTheme> ThemesCache
    {
        get => Themes;
        set
        {
            if (value != null && value.Count > 0)
                Themes = value;
        }
    }

    public static List<MeetingTheme> CreateDefaultThemes(string defaultBoxId = "433029580764")
    {
        return new List<MeetingTheme>
        {
            new() { Id = "employee_only", Name = "社員限定", BoxFolderId = defaultBoxId },
            new() { Id = "all_share", Name = "全体共有", BoxFolderId = defaultBoxId }
        };
    }

    /// <summary>
    /// 録音停止時にBoxへ自動アップロード＆スプール消去するか (デフォルト: true)
    /// </summary>
    public bool AutoUploadOnStop { get; set; } = true;

    /// <summary>
    /// Box接続時、OS/環境変数のプロキシ設定をバイパスして直接接続するか (デフォルト: false)
    /// ※Boxは外部クラウドのため、通常は標準プロキシ経由(false)で問題ありません
    /// </summary>
    public bool BypassProxy { get; set; } = false;

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

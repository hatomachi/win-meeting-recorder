namespace WinMeetingRecorder.Config;

/// <summary>
/// GitLab 連携設定
/// </summary>
public class GitLabConfig
{
    /// <summary>
    /// GitLab サーバーURL (例: https://gitlab.example.com または https://gitlab.com)
    /// </summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>
    /// プロジェクトID または URLエンコード前のパス (例: 12345 または mygroup/myproject)
    /// </summary>
    public string ProjectId { get; set; } = "";

    /// <summary>
    /// Personal Access Token (APIスコープ: api または write_repository)
    /// </summary>
    public string PersonalAccessToken { get; set; } = "";

    /// <summary>
    /// 送信先ブランチ (デフォルト: main)
    /// </summary>
    public string Branch { get; set; } = "main";

    /// <summary>
    /// リポジトリ内の保存先ベースディレクトリ (例: meetings。空なら直下)
    /// </summary>
    public string BasePath { get; set; } = "meetings";

    /// <summary>
    /// 社内GitLab等の自己署名SSL証明書エラーを無視するか (デフォルト: true)
    /// </summary>
    public bool IgnoreSslErrors { get; set; } = true;

    /// <summary>
    /// 録音停止時に自動でGitLabにアップロードするか (デフォルト: true)
    /// </summary>
    public bool AutoUploadOnStop { get; set; } = true;

    /// <summary>
    /// 設定が入力されているかどうか
    /// </summary>
    public bool IsConfigured => 
        !string.IsNullOrWhiteSpace(ServerUrl) && 
        !string.IsNullOrWhiteSpace(ProjectId) && 
        !string.IsNullOrWhiteSpace(PersonalAccessToken);
}

/// <summary>
/// アプリケーション全体設定
/// </summary>
public class AppConfig
{
    public GitLabConfig GitLab { get; set; } = new();
    public WhisperConfig Whisper { get; set; } = new();
    public AiConfig Ai { get; set; } = new();
}

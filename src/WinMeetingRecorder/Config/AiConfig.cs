namespace WinMeetingRecorder.Config;

/// <summary>
/// AI 議事録生成・修正設定 (GitHub Copilot CLI / Claude Code)
/// </summary>
public class AiConfig
{
    /// <summary>
    /// AI エンジン種別 ("copilot" または "claude")
    /// </summary>
    public string Engine { get; set; } = "copilot";

    /// <summary>
    /// 使用モデル (空文字の場合は各CLIの既定モデル。例: gpt-5, claude-3-7-sonnet など)
    /// </summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// CLI コマンドのカスタムパス (空文字の場合は自動探索)
    /// </summary>
    public string CustomCmdPath { get; set; } = "";

    /// <summary>
    /// 文字起こし完了後に自動で議事録を作成するか (デフォルト: true)
    /// </summary>
    public bool AutoGenerateMinutes { get; set; } = true;

    /// <summary>
    /// エンジン名が正しく設定されているか
    /// </summary>
    public bool IsValid =>
        string.Equals(Engine, "copilot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Engine, "claude", StringComparison.OrdinalIgnoreCase);
}

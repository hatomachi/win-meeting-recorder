namespace WinMeetingRecorder.Config;

/// <summary>
/// OpenAI Whisper API 互換文字起こし設定
/// </summary>
public class WhisperConfig
{
    /// <summary>
    /// Whisper API 互換サーバー Base URL (例: http://192.168.11.x:8000 または https://api.openai.com)
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// API キー (ローカル/社内サーバーで認証不要の場合は空文字でOK)
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// モデル名 (例: whisper-1, large-v3, medium, small, tiny 等。デフォルト: whisper-1)
    /// </summary>
    public string Model { get; set; } = "whisper-1";

    /// <summary>
    /// 音声認識言語 (デフォルト: ja)
    /// </summary>
    public string Language { get; set; } = "ja";

    /// <summary>
    /// 専門用語や文脈のプロンプトヒント (任意)
    /// </summary>
    public string Prompt { get; set; } = "";

    /// <summary>
    /// 社内/ローカルWhisperサーバーへの接続時、OS/環境変数のプロキシ設定をバイパスして直接接続するか (デフォルト: true)
    /// </summary>
    public bool BypassProxy { get; set; } = true;

    /// <summary>
    /// 会議終了時に自動で文字起こしを実行するか (デフォルト: true)
    /// </summary>
    public bool AutoTranscribeOnStop { get; set; } = true;

    /// <summary>
    /// 設定が入力されているかどうか
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

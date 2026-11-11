using System.Text.Json.Serialization;

namespace WinMeetingRecorder.Box;

/// <summary>
/// 後段の集約サーバーへセッション情報を通知するための meta.json スキーマ定義
/// (※ &lt;SessionId&gt;.zip 直下に音声・images/ と同梱してBoxへアップロードする)
/// </summary>
public class BoxMetaJson
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("theme_id")]
    public string ThemeId { get; set; } = "";

    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    [JsonPropertyName("start_time")]
    public string StartTime { get; set; } = "";

    [JsonPropertyName("duration_sec")]
    public int DurationSec { get; set; }

    [JsonPropertyName("audio_file")]
    public string AudioFile { get; set; } = "meeting_audio.mp3";

    [JsonPropertyName("image_count")]
    public int ImageCount { get; set; }

    [JsonPropertyName("image_files")]
    public List<BoxMetaImageItem> ImageFiles { get; set; } = new();
}

public class BoxMetaImageItem
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";
}

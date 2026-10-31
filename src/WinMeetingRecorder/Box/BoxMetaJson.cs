using System.Text.Json.Serialization;

namespace WinMeetingRecorder.Box;

/// <summary>
/// 後段の集約サーバーへ完了を通知するための meta.json スキーマ定義
/// (※全音声・画像アップロード後に必ず一番最後にBoxへアップロードする)
/// </summary>
public class BoxMetaJson
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("theme_id")]
    public string ThemeId { get; set; } = "";

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

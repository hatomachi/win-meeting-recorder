using System.Text.Json.Serialization;

namespace WinMeetingRecorder.Box;

/// <summary>
/// themes.json に定義される会議テーマ台帳モデル
/// </summary>
public class MeetingTheme
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("box_folder_id")]
    public string BoxFolderId { get; set; } = "";
}

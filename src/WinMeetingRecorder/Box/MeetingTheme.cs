using System.Text.Json.Serialization;

namespace WinMeetingRecorder.Box;

/// <summary>
/// config.json に定義される会議テーマ設定モデル
/// </summary>
public class MeetingTheme
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("box_folder_id")]
    public string BoxFolderId { get; set; } = "";

    /// <summary>
    /// config.json で "box_id" または "folder_id" と記載されても読み書きできるようにするエイリアス
    /// </summary>
    [JsonPropertyName("box_id")]
    public string? BoxId
    {
        get => BoxFolderId;
        set
        {
            if (!string.IsNullOrEmpty(value))
                BoxFolderId = value;
        }
    }

    [JsonPropertyName("folder_id")]
    public string? FolderId
    {
        get => BoxFolderId;
        set
        {
            if (!string.IsNullOrEmpty(value))
                BoxFolderId = value;
        }
    }
}

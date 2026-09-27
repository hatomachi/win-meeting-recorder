namespace WinMeetingRecorder.Spool;

/// <summary>
/// 会議セッションのメタデータ定義 (スプールディレクトリ内の metadata.json)
/// </summary>
public class MeetingSessionMetadata
{
    public string SessionId { get; set; } = "";
    public string SpoolDirectory { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double DurationSeconds { get; set; }
    public string AudioFileName { get; set; } = "meeting_audio.wav";
    public bool HasAudio { get; set; }
    public int MonitorIndex { get; set; }
    public string? MonitorName { get; set; }
    public List<SessionImageItem> Images { get; set; } = new();
}

/// <summary>
/// キャプチャされたスクショの情報
/// </summary>
public class SessionImageItem
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public int ElapsedSeconds { get; set; }
    public string Reason { get; set; } = "";
    public double Diff { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// スプール内セッション概要情報 (一覧表示用)
/// </summary>
public class PendingSessionSummary
{
    public string SessionId { get; set; } = "";
    public string DirectoryPath { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int ScreenshotCount { get; set; }
    public bool HasAudio { get; set; }
    public long TotalSizeBytes { get; set; }
}

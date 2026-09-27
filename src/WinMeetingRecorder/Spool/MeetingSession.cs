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
    public string? Mp3FileName { get; set; } = "meeting_audio.mp3";
    public bool HasMp3 { get; set; }
    public string? TranscriptFileName { get; set; } = "transcript.json";
    public bool HasTranscript { get; set; }
    public string? MinutesFileName { get; set; } = "MINUTES.md";
    public bool HasMinutes { get; set; }
    public string? AiSessionId { get; set; }
    public PipelineStatus Pipeline { get; set; } = new();
    public int MonitorIndex { get; set; }
    public string? MonitorName { get; set; }
    public List<SessionImageItem> Images { get; set; } = new();
}

/// <summary>
/// パイプライン各ステップの進行状態
/// </summary>
public class PipelineStatus
{
    // "pending" | "running" | "success" | "error" | "skipped"
    public string Audio { get; set; } = "pending";
    public string Transcribe { get; set; } = "pending";
    public string Minutes { get; set; } = "pending";
    public string Upload { get; set; } = "pending";
    public string? LastStep { get; set; }
    public string? ErrorMessage { get; set; }
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
    public bool HasMp3 { get; set; }
    public bool HasTranscript { get; set; }
    public bool HasMinutes { get; set; }
    public PipelineStatus Pipeline { get; set; } = new();
    public long TotalSizeBytes { get; set; }
}

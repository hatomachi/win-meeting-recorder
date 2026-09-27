using System.Text.Json;
using WinMeetingRecorder.Screen;

namespace WinMeetingRecorder.Spool;

/// <summary>
/// ローカルスプール管理サービス (%AppData%\WinMeetingRecorder\spool\<sessionId>)
/// </summary>
public class SpoolService
{
    private readonly string _spoolBaseDir;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SpoolService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            appData = AppContext.BaseDirectory;
        }

        _spoolBaseDir = Path.Combine(appData, "WinMeetingRecorder", "spool");
        Directory.CreateDirectory(_spoolBaseDir);
    }

    public string SpoolBaseDir => _spoolBaseDir;

    /// <summary>
    /// 新規会議セッションを初期化し、スプールディレクトリを作成します
    /// </summary>
    public MeetingSessionMetadata CreateSession(int monitorIndex = 0, string? monitorName = null)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            var sessionId = now.ToString("yyyyMMdd_HHmmss");
            var sessionDir = Path.Combine(_spoolBaseDir, sessionId);
            Directory.CreateDirectory(sessionDir);
            Directory.CreateDirectory(Path.Combine(sessionDir, "images"));

            var metadata = new MeetingSessionMetadata
            {
                SessionId = sessionId,
                SpoolDirectory = sessionDir,
                StartTime = now,
                MonitorIndex = monitorIndex,
                MonitorName = monitorName
            };

            SaveMetadata(metadata);
            return metadata;
        }
    }

    /// <summary>
    /// 撮影されたスクリーンショット情報をセッションメタデータに追記します
    /// </summary>
    public void RecordImageCaptured(string sessionDirOrId, ScreenCaptureEvent evt)
    {
        lock (_lock)
        {
            var metadata = GetSessionMetadata(sessionDirOrId);
            if (metadata == null) return;

            metadata.Images.Add(new SessionImageItem
            {
                FileName = evt.FileName,
                RelativePath = evt.RelativePath,
                ElapsedSeconds = evt.ElapsedSeconds,
                Reason = evt.Reason,
                Diff = evt.Diff,
                Timestamp = evt.Timestamp
            });

            SaveMetadata(metadata);
        }
    }

    /// <summary>
    /// 録音・キャプチャ終了時にメタデータを確定します
    /// </summary>
    public MeetingSessionMetadata? FinishSession(string sessionDirOrId)
    {
        lock (_lock)
        {
            var metadata = GetSessionMetadata(sessionDirOrId);
            if (metadata == null) return null;

            var now = DateTime.Now;
            metadata.EndTime = now;
            metadata.DurationSeconds = (now - metadata.StartTime).TotalSeconds;

            var audioPath = Path.Combine(metadata.SpoolDirectory, metadata.AudioFileName);
            metadata.HasAudio = File.Exists(audioPath);

            var mp3Path = Path.Combine(metadata.SpoolDirectory, metadata.Mp3FileName ?? "meeting_audio.mp3");
            metadata.HasMp3 = File.Exists(mp3Path);

            var transcriptPath = Path.Combine(metadata.SpoolDirectory, metadata.TranscriptFileName ?? "transcript.json");
            metadata.HasTranscript = File.Exists(transcriptPath);

            var minutesPath = Path.Combine(metadata.SpoolDirectory, metadata.MinutesFileName ?? "MINUTES.md");
            metadata.HasMinutes = File.Exists(minutesPath);

            SaveMetadata(metadata);
            return metadata;
        }
    }

    /// <summary>
    /// メタデータを明示的に更新・保存します
    /// </summary>
    public void UpdateMetadata(MeetingSessionMetadata metadata)
    {
        lock (_lock)
        {
            var audioPath = Path.Combine(metadata.SpoolDirectory, metadata.AudioFileName);
            metadata.HasAudio = File.Exists(audioPath);

            var mp3Path = Path.Combine(metadata.SpoolDirectory, metadata.Mp3FileName ?? "meeting_audio.mp3");
            metadata.HasMp3 = File.Exists(mp3Path);

            var transcriptPath = Path.Combine(metadata.SpoolDirectory, metadata.TranscriptFileName ?? "transcript.json");
            metadata.HasTranscript = File.Exists(transcriptPath);

            var minutesPath = Path.Combine(metadata.SpoolDirectory, metadata.MinutesFileName ?? "MINUTES.md");
            metadata.HasMinutes = File.Exists(minutesPath);

            SaveMetadata(metadata);
        }
    }

    /// <summary>
    /// 指定されたセッションディレクトリまたはIDのメタデータを取得します。
    /// metadata.json が存在しない場合、ディレクトリ内を走査して復元します。
    /// </summary>
    public MeetingSessionMetadata? GetSessionMetadata(string sessionDirOrId)
    {
        var sessionDir = ResolveSessionDir(sessionDirOrId);
        if (!Directory.Exists(sessionDir))
        {
            return null;
        }

        var metaPath = Path.Combine(sessionDir, "metadata.json");
        if (File.Exists(metaPath))
        {
            try
            {
                var json = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<MeetingSessionMetadata>(json, JsonOptions);
                if (meta != null)
                {
                    meta.SpoolDirectory = sessionDir;
                    meta.HasMp3 = File.Exists(Path.Combine(sessionDir, meta.Mp3FileName ?? "meeting_audio.mp3"));
                    meta.HasTranscript = File.Exists(Path.Combine(sessionDir, meta.TranscriptFileName ?? "transcript.json"));
                    meta.HasMinutes = File.Exists(Path.Combine(sessionDir, meta.MinutesFileName ?? "MINUTES.md"));
                    return meta;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpoolService] metadata.json 読み込み警告: {ex.Message}");
            }
        }

        // フォールバック: ディレクトリをスキャンして再構成
        var dirInfo = new DirectoryInfo(sessionDir);
        var recovered = new MeetingSessionMetadata
        {
            SessionId = dirInfo.Name,
            SpoolDirectory = sessionDir,
            StartTime = dirInfo.CreationTime,
            EndTime = dirInfo.LastWriteTime,
            DurationSeconds = (dirInfo.LastWriteTime - dirInfo.CreationTime).TotalSeconds
        };

        var audioFile = Path.Combine(sessionDir, "meeting_audio.wav");
        recovered.HasAudio = File.Exists(audioFile);

        var mp3File = Path.Combine(sessionDir, "meeting_audio.mp3");
        recovered.HasMp3 = File.Exists(mp3File);

        var transcriptFile = Path.Combine(sessionDir, "transcript.json");
        recovered.HasTranscript = File.Exists(transcriptFile);

        var minutesFile = Path.Combine(sessionDir, "MINUTES.md");
        recovered.HasMinutes = File.Exists(minutesFile);

        var imagesDir = Path.Combine(sessionDir, "images");
        if (Directory.Exists(imagesDir))
        {
            var imageFiles = Directory.GetFiles(imagesDir, "*.jpg").OrderBy(f => f);
            foreach (var imgPath in imageFiles)
            {
                var fi = new FileInfo(imgPath);
                recovered.Images.Add(new SessionImageItem
                {
                    FileName = fi.Name,
                    RelativePath = Path.Combine("images", fi.Name),
                    ElapsedSeconds = (int)(fi.CreationTime - recovered.StartTime).TotalSeconds,
                    Reason = "recovered",
                    Timestamp = fi.CreationTime
                });
            }
        }

        return recovered;
    }

    /// <summary>
    /// スプール内の未送信セッション一覧を取得します
    /// </summary>
    public List<PendingSessionSummary> GetPendingSessions()
    {
        var list = new List<PendingSessionSummary>();
        if (!Directory.Exists(_spoolBaseDir))
        {
            return list;
        }

        foreach (var subDir in Directory.GetDirectories(_spoolBaseDir))
        {
            var dirInfo = new DirectoryInfo(subDir);
            var meta = GetSessionMetadata(subDir);
            long totalBytes = 0;

            try
            {
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += file.Length;
                }
            }
            catch { /* ignore */ }

            list.Add(new PendingSessionSummary
            {
                SessionId = dirInfo.Name,
                DirectoryPath = subDir,
                CreatedAt = meta?.StartTime ?? dirInfo.CreationTime,
                ScreenshotCount = meta?.Images.Count ?? 0,
                HasAudio = meta?.HasAudio ?? File.Exists(Path.Combine(subDir, "meeting_audio.wav")),
                HasMp3 = meta?.HasMp3 ?? File.Exists(Path.Combine(subDir, "meeting_audio.mp3")),
                HasTranscript = meta?.HasTranscript ?? File.Exists(Path.Combine(subDir, "transcript.json")),
                HasMinutes = meta?.HasMinutes ?? File.Exists(Path.Combine(subDir, "MINUTES.md")),
                Pipeline = meta?.Pipeline ?? new PipelineStatus(),
                TotalSizeBytes = totalBytes
            });
        }

        return list.OrderByDescending(x => x.CreatedAt).ToList();
    }

    /// <summary>
    /// セッションディレクトリを安全に削除します (GitLab送信成功時)
    /// </summary>
    public bool DeleteSession(string sessionDirOrId)
    {
        lock (_lock)
        {
            var dir = ResolveSessionDir(sessionDirOrId);
            if (!Directory.Exists(dir))
            {
                return false;
            }

            try
            {
                Directory.Delete(dir, true);
                Console.WriteLine($"[SpoolService] ✅ スプールを正常に削除しました: {dir}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpoolService] ❌ スプール削除失敗: {ex.Message}");
                return false;
            }
        }
    }

    private void SaveMetadata(MeetingSessionMetadata metadata)
    {
        try
        {
            var metaPath = Path.Combine(metadata.SpoolDirectory, "metadata.json");
            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            File.WriteAllText(metaPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpoolService] metadata.json 保存警告: {ex.Message}");
        }
    }

    private string ResolveSessionDir(string sessionDirOrId)
    {
        if (Directory.Exists(sessionDirOrId))
        {
            return Path.GetFullPath(sessionDirOrId);
        }

        return Path.Combine(_spoolBaseDir, sessionDirOrId);
    }
}

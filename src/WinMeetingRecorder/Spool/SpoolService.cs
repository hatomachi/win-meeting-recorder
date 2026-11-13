using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinMeetingRecorder.Screen;
using WinMeetingRecorder.Whisper;

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
    public MeetingSessionMetadata CreateSession(int monitorIndex = 0, string? monitorName = null, string? memo = null, string? themeId = null, string? themeName = null)
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
                Memo = memo,
                ThemeId = themeId,
                ThemeName = themeName,
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
            var transcriptYamlPath = Path.Combine(metadata.SpoolDirectory, "transcript.yaml");
            metadata.HasTranscript = File.Exists(transcriptPath) || File.Exists(transcriptYamlPath);

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
            var transcriptYamlPath = Path.Combine(metadata.SpoolDirectory, "transcript.yaml");
            metadata.HasTranscript = File.Exists(transcriptPath) || File.Exists(transcriptYamlPath);

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
        var transcriptYamlFile = Path.Combine(sessionDir, "transcript.yaml");
        recovered.HasTranscript = File.Exists(transcriptFile) || File.Exists(transcriptYamlFile);

        // transcript.json があり transcript.yaml がない場合は自動生成 (MINUTES_GUIDE互換)
        if (File.Exists(transcriptFile) && !File.Exists(transcriptYamlFile))
        {
            try
            {
                var json = File.ReadAllText(transcriptFile);
                var tr = JsonSerializer.Deserialize<WhisperTranscriptionResponse>(json, JsonOptions);
                if (tr?.Segments != null && tr.Segments.Count > 0)
                {
                    var yaml = WhisperService.ToYamlPatternA(tr.Segments);
                    File.WriteAllText(transcriptYamlFile, yaml, Encoding.UTF8);
                }
            }
            catch { /* ignore */ }
        }

        var minutesFile = Path.Combine(sessionDir, "MINUTES.md");
        recovered.HasMinutes = File.Exists(minutesFile);

        var imagesDir = Path.Combine(sessionDir, "images");
        if (Directory.Exists(imagesDir))
        {
            var imageFiles = Directory.GetFiles(imagesDir, "*.jpg").OrderBy(f => f);
            foreach (var imgPath in imageFiles)
            {
                var fi = new FileInfo(imgPath);
                var fallbackElapsed = (int)(fi.CreationTime - recovered.StartTime).TotalSeconds;
                var elapsed = ParseElapsedSecondsFromFileName(fi.Name, fallbackElapsed);
                recovered.Images.Add(new SessionImageItem
                {
                    FileName = fi.Name,
                    RelativePath = Path.Combine("images", fi.Name),
                    ElapsedSeconds = elapsed,
                    Reason = "recovered",
                    Timestamp = fi.CreationTime
                });
            }
        }

        return recovered;
    }

    /// <summary>
    /// 画像ファイル名から経過秒数を解析します (MINUTES_GUIDE準拠: HH_MM_SS_screen.jpg または MM_SS_screen.jpg)
    /// </summary>
    public static int ParseElapsedSecondsFromFileName(string fileName, int fallback = 0)
    {
        var hmsMatch = Regex.Match(fileName, @"(?:^|[^0-9])(\d{1,2})[-_:.](\d{2})[-_:.](\d{2})(?:[^0-9]|$)");
        if (hmsMatch.Success)
        {
            int h = int.Parse(hmsMatch.Groups[1].Value);
            int m = int.Parse(hmsMatch.Groups[2].Value);
            int s = int.Parse(hmsMatch.Groups[3].Value);
            if (m < 60 && s < 60)
            {
                return h * 3600 + m * 60 + s;
            }
        }

        var msMatch = Regex.Match(fileName, @"(?:^|[^0-9])(\d{1,2})[-_:.](\d{2})(?:[^0-9]|$)");
        if (msMatch.Success)
        {
            int m = int.Parse(msMatch.Groups[1].Value);
            int s = int.Parse(msMatch.Groups[2].Value);
            if (s < 60)
            {
                return m * 60 + s;
            }
        }

        return fallback;
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
                Memo = meta?.Memo,
                ThemeId = meta?.ThemeId,
                ThemeName = meta?.ThemeName,
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
    /// 指定セッションの撮影済みスクリーンショット一覧を取得します (実ファイル存在確認・補完付き)
    /// </summary>
    public List<SessionImageItem> GetSessionImages(string sessionDirOrId)
    {
        lock (_lock)
        {
            var metadata = GetSessionMetadata(sessionDirOrId);
            if (metadata == null)
            {
                return new List<SessionImageItem>();
            }

            var imagesDir = Path.Combine(metadata.SpoolDirectory, "images");
            if (!Directory.Exists(imagesDir))
            {
                return new List<SessionImageItem>();
            }

            var result = new List<SessionImageItem>();
            var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in metadata.Images)
            {
                if (string.IsNullOrWhiteSpace(item.FileName) || !IsSafePathSegment(item.FileName))
                {
                    continue;
                }

                var fullPath = Path.Combine(imagesDir, item.FileName);
                if (File.Exists(fullPath) && knownFiles.Add(item.FileName))
                {
                    result.Add(item);
                }
            }

            // メタデータ未反映のファイルがあればファイル名順で補完
            try
            {
                var diskFiles = Directory.GetFiles(imagesDir, "*.jpg")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

                foreach (var imgPath in diskFiles)
                {
                    var fi = new FileInfo(imgPath);
                    if (!knownFiles.Add(fi.Name))
                    {
                        continue;
                    }

                    var fallbackElapsed = Math.Max(0, (int)(fi.CreationTime - metadata.StartTime).TotalSeconds);
                    var elapsed = ParseElapsedSecondsFromFileName(fi.Name, fallbackElapsed);
                    result.Add(new SessionImageItem
                    {
                        FileName = fi.Name,
                        RelativePath = Path.Combine("images", fi.Name),
                        ElapsedSeconds = elapsed,
                        Reason = "captured",
                        Timestamp = fi.CreationTime
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpoolService] 画像ファイル走査警告: {ex.Message}");
            }

            return result;
        }
    }

    /// <summary>
    /// 指定セッション・ファイル名のスクリーンショットの安全なフルパスを取得します (ディレクトリトラバーサル防止)
    /// </summary>
    public string? GetSessionImageFilePath(string sessionId, string fileName)
    {
        if (!IsSafePathSegment(sessionId) || !IsSafePathSegment(fileName))
        {
            return null;
        }

        var ext = Path.GetExtension(fileName);
        if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sessionDir = Path.GetFullPath(Path.Combine(_spoolBaseDir, sessionId));
        var spoolBaseFull = Path.GetFullPath(_spoolBaseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!sessionDir.StartsWith(spoolBaseFull, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var imagesDir = Path.GetFullPath(Path.Combine(sessionDir, "images"));
        var imagesDirPrefix = imagesDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var targetFile = Path.GetFullPath(Path.Combine(imagesDir, fileName));

        if (!targetFile.StartsWith(imagesDirPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return File.Exists(targetFile) ? targetFile : null;
    }

    /// <summary>
    /// セッションのメモを更新・保存します
    /// </summary>
    public bool UpdateSessionMemo(string sessionDirOrId, string? memo)
    {
        lock (_lock)
        {
            var metadata = GetSessionMetadata(sessionDirOrId);
            if (metadata == null) return false;

            metadata.Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
            SaveMetadata(metadata);
            return true;
        }
    }

    /// <summary>
    /// セッションのテーマ（保存先フォルダ）を更新・保存します
    /// </summary>
    public bool UpdateSessionTheme(string sessionDirOrId, string? themeId, string? themeName = null)
    {
        lock (_lock)
        {
            var metadata = GetSessionMetadata(sessionDirOrId);
            if (metadata == null) return false;

            metadata.ThemeId = string.IsNullOrWhiteSpace(themeId) ? null : themeId.Trim();
            if (!string.IsNullOrWhiteSpace(themeName))
            {
                metadata.ThemeName = themeName.Trim();
            }
            SaveMetadata(metadata);
            return true;
        }
    }

    /// <summary>
    /// セッションディレクトリ（および関連ローカルZIP）を安全に削除します
    /// </summary>
    public bool DeleteSession(string sessionDirOrId)
    {
        lock (_lock)
        {
            var dir = ResolveSessionDir(sessionDirOrId);
            var trimmedDir = Path.TrimEndingDirectorySeparator(dir);
            var parentDir = Directory.GetParent(trimmedDir)?.FullName ?? _spoolBaseDir;
            var sessionName = new DirectoryInfo(trimmedDir).Name;
            var zipPath = Path.Combine(parentDir, $"{sessionName}.zip");
            var tempZipPath = zipPath + ".tmp";

            bool dirExists = Directory.Exists(dir);
            bool zipExists = File.Exists(zipPath);
            if (!dirExists && !zipExists)
            {
                return false;
            }

            try
            {
                if (dirExists)
                {
                    Directory.Delete(dir, true);
                }
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
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

    /// <summary>
    /// 指定されたセッションIDのディレクトリが存在するか確認します
    /// </summary>
    public bool SessionExists(string sessionId)
    {
        if (!IsSafePathSegment(sessionId))
        {
            return false;
        }
        var dir = Path.Combine(_spoolBaseDir, sessionId);
        return Directory.Exists(dir);
    }

    private static bool IsSafePathSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        if (segment.Contains("..") || segment.Contains('/') || segment.Contains('\\'))
        {
            return false;
        }

        return Path.GetFileName(segment) == segment;
    }

    private string ResolveSessionDir(string sessionDirOrId)
    {
        if (Path.IsPathRooted(sessionDirOrId) && Directory.Exists(sessionDirOrId))
        {
            return Path.GetFullPath(sessionDirOrId);
        }

        if (!IsSafePathSegment(sessionDirOrId))
        {
            return Path.Combine(_spoolBaseDir, "__invalid_session__");
        }

        return Path.Combine(_spoolBaseDir, sessionDirOrId);
    }
}

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace WinMeetingRecorder.Logging;

/// <summary>
/// アプリ全体の軽量動作ログ管理サービス
/// メモリ上に直近ログを保持しつつ、当日のログファイル (%AppData%\WinMeetingRecorder\logs) にも永続化する
/// </summary>
public static class AppLogger
{
    private static readonly ConcurrentQueue<string> MemoryLogs = new();
    private const int MaxMemoryLogCount = 500;
    private static readonly object FileLock = new();
    private static string? _logDirectory;

    public static string LogDirectory
    {
        get
        {
            if (_logDirectory == null)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _logDirectory = Path.Combine(appData, "WinMeetingRecorder", "logs");
                if (!Directory.Exists(_logDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(_logDirectory);
                    }
                    catch { /* ignore */ }
                }
            }
            return _logDirectory;
        }
    }

    private static string TodayLogFilePath =>
        Path.Combine(LogDirectory, $"app_{DateTime.Now:yyyyMMdd}.log");

    static AppLogger()
    {
        // 起動時に当日のログファイルが存在すれば、末尾最新100行をメモリキューへ復元
        try
        {
            if (File.Exists(TodayLogFilePath))
            {
                var lines = File.ReadAllLines(TodayLogFilePath);
                var recent = lines.TakeLast(100);
                foreach (var line in recent)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        MemoryLogs.Enqueue(line);
                    }
                }
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// 通常ログを出力
    /// </summary>
    public static void Info(string message) => Log(message, "INFO");

    /// <summary>
    /// 警告ログを出力
    /// </summary>
    public static void Warn(string message) => Log(message, "WARN");

    /// <summary>
    /// エラーログを出力
    /// </summary>
    public static void Error(string message) => Log(message, "ERROR");

    /// <summary>
    /// フォーマット済みタイムスタンプ付きでログを出力
    /// </summary>
    public static void Log(string message, string level = "INFO")
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formatted = $"[{timestamp}] {message}";

        // 1. コンソール出力 (CLI・デバッグ用)
        try
        {
            Console.WriteLine(formatted);
        }
        catch { /* ignore */ }

        // 2. メモリキューに追加
        MemoryLogs.Enqueue(formatted);
        while (MemoryLogs.Count > MaxMemoryLogCount)
        {
            MemoryLogs.TryDequeue(out _);
        }

        // 3. 当日のログファイルへ追記
        try
        {
            lock (FileLock)
            {
                File.AppendAllText(TodayLogFilePath, formatted + Environment.NewLine);
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// 直近のログ一覧を取得
    /// </summary>
    public static IReadOnlyList<string> GetRecentLogs(int limit = 100)
    {
        int count = Math.Min(Math.Max(limit, 1), MaxMemoryLogCount);
        return MemoryLogs.TakeLast(count).ToList();
    }

    /// <summary>
    /// メモリ上のログをクリア
    /// </summary>
    public static void ClearMemoryLogs()
    {
        while (MemoryLogs.TryDequeue(out _)) { }
    }
}

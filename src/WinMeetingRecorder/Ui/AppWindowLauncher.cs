using System.Diagnostics;

namespace WinMeetingRecorder.Ui;

/// <summary>
/// 管理パネル (Web UI) をブラウザの通常タブではなく、アドレスバーやタブのない
/// 独立した専用デスクトップアプリウィンドウ (--app モード) として起動するランチャー
/// </summary>
public static class AppWindowLauncher
{
    private static readonly string[] EdgeCandidates = new[]
    {
        "msedge.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    };

    private static readonly string[] ChromeCandidates = new[]
    {
        "chrome.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    };

    /// <summary>
    /// 管理パネルを独自アプリウィンドウとして起動
    /// </summary>
    /// <param name="url">起動URL (デフォルト: http://localhost:5000)</param>
    /// <param name="width">ウィンドウ初期幅 (px)</param>
    /// <param name="height">ウィンドウ初期高さ (px)</param>
    public static bool Open(string url = "http://localhost:5000", int width = 680, int height = 860)
    {
        string arguments = $"--app=\"{url}\" --window-size={width},{height}";

        // 1. Edge のアプリモード (--app) を試行 (Windows 10/11 なら標準搭載)
        foreach (var candidate in EdgeCandidates)
        {
            if (TryLaunch(candidate, arguments))
            {
                Console.WriteLine($"[AppWindowLauncher] 🌐 Edge アプリモードで専用ウィンドウを起動しました ({candidate})");
                return true;
            }
        }

        // 2. Chrome のアプリモード (--app) を試行
        foreach (var candidate in ChromeCandidates)
        {
            if (TryLaunch(candidate, arguments))
            {
                Console.WriteLine($"[AppWindowLauncher] 🌐 Chrome アプリモードで専用ウィンドウを起動しました ({candidate})");
                return true;
            }
        }

        // 3. フォールバック: 通常の既定ブラウザでオープン
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Console.WriteLine("[AppWindowLauncher] ⚠️ 専用ウィンドウの起動に失敗したため、既定ブラウザでオープンしました。");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppWindowLauncher] ❌ 操作盤の起動に失敗しました: {ex.Message}");
            return false;
        }
    }

    private static bool TryLaunch(string fileName, string arguments)
    {
        try
        {
            // フルパス指定でファイルが存在しない場合は早期スキップ
            if (Path.IsPathRooted(fileName) && !File.Exists(fileName))
            {
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };

            var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }
}

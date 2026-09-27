using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WinMeetingRecorder.Screen;

/// <summary>
/// ディスプレイ（画面）の列挙・情報取得サービス
/// </summary>
[SupportedOSPlatform("windows")]
public static class ScreenService
{
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private const int MONITORINFOF_PRIMARY = 0x00000001;

    // System Metrics constants
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>
    /// 接続されているすべての物理/論理ディスプレイを列挙します
    /// </summary>
    public static List<ScreenInfo> GetScreens()
    {
        var screens = new List<ScreenInfo>();

        try
        {
            int monitorIndex = 0;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));

                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                    var bounds = new Rectangle(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Width, mi.rcMonitor.Height);
                    string name = string.IsNullOrWhiteSpace(mi.szDevice) ? $"Display {monitorIndex + 1}" : mi.szDevice;

                    screens.Add(new ScreenInfo(monitorIndex, name, bounds, isPrimary));
                    monitorIndex++;
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // フォールバック: Win32 API 失敗時はプライマリのみ生成
        }

        if (screens.Count == 0)
        {
            screens.Add(GetPrimaryScreen());
        }

        return screens;
    }

    /// <summary>
    /// プライマリ（メイン）ディスプレイの情報を取得します
    /// </summary>
    public static ScreenInfo GetPrimaryScreen()
    {
        try
        {
            int width = GetSystemMetrics(SM_CXSCREEN);
            int height = GetSystemMetrics(SM_CYSCREEN);
            if (width > 0 && height > 0)
            {
                return new ScreenInfo(0, "Primary Display", new Rectangle(0, 0, width, height), true);
            }
        }
        catch
        {
            // ignore
        }

        return new ScreenInfo(0, "Primary Display", new Rectangle(0, 0, 1920, 1080), true);
    }

    /// <summary>
    /// 全モニターを結合した仮想デスクトップ領域の情報を取得します
    /// </summary>
    public static ScreenInfo GetVirtualDesktop()
    {
        try
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (width > 0 && height > 0)
            {
                return new ScreenInfo(-1, "Virtual Desktop (All Monitors)", new Rectangle(x, y, width, height), false);
            }
        }
        catch
        {
            // ignore
        }

        return GetPrimaryScreen() with { Index = -1, Name = "Virtual Desktop (All Monitors)" };
    }

    /// <summary>
    /// 指定されたインデックスの画面情報を解決します (0: プライマリ, 1..N: 各画面, -1: 仮想デスクトップ)
    /// </summary>
    public static ScreenInfo ResolveScreen(int monitorIndex)
    {
        if (monitorIndex == -1)
        {
            return GetVirtualDesktop();
        }

        var screens = GetScreens();
        if (monitorIndex >= 0 && monitorIndex < screens.Count)
        {
            return screens[monitorIndex];
        }

        // 見つからない場合はプライマリ
        var primary = screens.FirstOrDefault(s => s.IsPrimary);
        return primary ?? (screens.Count > 0 ? screens[0] : GetPrimaryScreen());
    }
}

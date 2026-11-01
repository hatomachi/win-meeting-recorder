using System.Runtime.InteropServices;

namespace WinMeetingRecorder.Workflow;

/// <summary>
/// Windows 電源管理・スリープ抑止サービス
/// 会議記録中にPCが自動的にスリープしたりディスプレイの電源が切れて
/// 録音や画面キャプチャが停止する悲劇をOSレベルで防止します。
/// </summary>
public static class PowerManagementService
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    public static bool IsSleepPrevented { get; private set; }

    /// <summary>
    /// システムスリープおよびディスプレイ電源OFFを抑止します（録音開始時に呼び出し）
    /// </summary>
    public static bool PreventSleepAndDisplayOff()
    {
        try
        {
            uint prev = SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            IsSleepPrevented = prev != 0;
            return IsSleepPrevented;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PowerManagement] スリープ抑止設定警告: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 定期ハートビートとしてスリープ・画面OFF抑止タイマーをリセットします
    /// </summary>
    public static void PulseKeepAlive()
    {
        try
        {
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            IsSleepPrevented = true;
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// スリープ抑止を解除し、OSの通常電源管理状態に戻します（録音停止・アプリ終了時に呼び出し）
    /// </summary>
    public static void RestorePowerSettings()
    {
        try
        {
            SetThreadExecutionState(ES_CONTINUOUS);
            IsSleepPrevented = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PowerManagement] スリープ抑止解除警告: {ex.Message}");
        }
    }
}

using System.Drawing;

namespace WinMeetingRecorder.Screen;

/// <summary>
/// ディスプレイ（モニター）の基本情報
/// </summary>
/// <param name="Index">モニターのインデックス (0: プライマリ, 1..N: 個別, -1: 仮想デスクトップ全体)</param>
/// <param name="Name">ディスプレイドライバ上のデバイス名</param>
/// <param name="Bounds">画面のピクセル領域 (X, Y, Width, Height)</param>
/// <param name="IsPrimary">プライマリモニターかどうか</param>
public record ScreenInfo(int Index, string Name, Rectangle Bounds, bool IsPrimary)
{
    public override string ToString()
    {
        string primaryBadge = IsPrimary ? " (★既定/プライマリ)" : "";
        return $"[{Index}] {Name}{primaryBadge} - {Bounds.Width}x{Bounds.Height} at ({Bounds.X}, {Bounds.Y})";
    }
}

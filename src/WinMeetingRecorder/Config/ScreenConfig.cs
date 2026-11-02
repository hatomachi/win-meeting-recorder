namespace WinMeetingRecorder.Config;

/// <summary>
/// 画面キャプチャ＆差分検知設定
/// </summary>
public class ScreenConfig
{
    /// <summary>
    /// 対象モニター (0: プライマリ, 1..N: 個別画面, -1: 仮想デスクトップ全体)
    /// </summary>
    public int MonitorIndex { get; set; } = 0;

    /// <summary>
    /// 画面監視周期ミリ秒 (デフォルト 5000ms = 5秒)
    /// </summary>
    public int CheckIntervalMs { get; set; } = 5000;

    /// <summary>
    /// 画面変化がない場合の定期キーフレーム保存間隔秒数 (0で無効化。デフォルト 0)
    /// </summary>
    public int MaxIntervalSec { get; set; } = 0;

    /// <summary>
    /// 差分検知アルゴリズム ("phash" = pHash 8x8 DCT, "hybrid" = 従来輝度差ハイブリッド)
    /// </summary>
    public string DiffAlgorithm { get; set; } = "phash";

    /// <summary>
    /// pHash ハミング距離閾値 (ハミング距離 > PhashThreshold で変化検知。デフォルト 5)
    /// </summary>
    public int PhashThreshold { get; set; } = 5;

    /// <summary>
    /// 従来hybrid方式の変化検知閾値 (0.05 = 5%以上の変化で検知。デフォルト 0.05)
    /// </summary>
    public double DiffThreshold { get; set; } = 0.05;

    /// <summary>
    /// 変化検知後の最小クールダウン秒数 (過剰連写防止。デフォルト 2秒)
    /// </summary>
    public int MinIntervalSec { get; set; } = 2;

    /// <summary>
    /// スライド変化検知後の安定待ちデバウンス時間ミリ秒 (デフォルト 500ms)
    /// </summary>
    public int DebounceStabilityMs { get; set; } = 500;

    /// <summary>
    /// フル解像度JPEGの保存品質 (1〜100、デフォルト 80)
    /// </summary>
    public long JpegQuality { get; set; } = 80L;
}

namespace DeepSeekBalance.Models;

/// <summary>
/// 余额预警提醒提供方的设置（由 ClassIsland 随应用设置持久化）。
/// </summary>
public class DeepSeekAlertNotificationProviderSettings
{
    /// <summary>
    /// 遮罩标题文案。
    /// </summary>
    public string MaskText { get; set; } = "API余额不足预警";

    /// <summary>
    /// 正文内容文案；其中的 &lt;阈值&gt;（或 {0}）会在发送时替换为当前预警阈值。
    /// </summary>
    public string OverlayText { get; set; } = "您的 DeepSeek API 余额不足<阈值>元，为避免影响使用，请尽快充值。";

    /// <summary>
    /// 遮罩显示时长（秒）。
    /// </summary>
    public int MaskDurationSeconds { get; set; } = 10;

    /// <summary>
    /// 正文显示时长（秒）。
    /// </summary>
    public int OverlayDurationSeconds { get; set; } = 10;
}

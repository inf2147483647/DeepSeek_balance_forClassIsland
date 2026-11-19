namespace DeepSeekBalance.Models;

/// <summary>
/// 余额阈值预警的插件级设置（持久化到插件配置目录的 AlertSettings.json）。
/// </summary>
public class DeepSeekAlertSettings
{
    /// <summary>
    /// 是否启用余额阈值预警。
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 预警阈值（元）。余额不高于此值时触发提醒；0 表示余额耗尽时提醒。
    /// </summary>
    public decimal Threshold { get; set; } = 5m;

    /// <summary>
    /// 是否通过 Windows 系统通知提醒（Win10+ toast，Win8.1- 托盘气泡）。
    /// </summary>
    public bool NotifyViaWindows { get; set; } = true;

    /// <summary>
    /// 是否通过 ClassIsland 提醒（遮罩 + 正文）提醒。
    /// </summary>
    public bool NotifyViaClassIsland { get; set; } = true;
}

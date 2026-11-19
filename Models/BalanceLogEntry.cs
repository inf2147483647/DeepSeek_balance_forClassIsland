using System;
using System.Globalization;

namespace DeepSeekBalance.Models;

/// <summary>
/// 一条余额变动日志：记录了某次充值或消费发生的时间、金额与变动后的余额。
/// </summary>
public class BalanceLogEntry
{
    /// <summary>
    /// 变动发生时间（本地时间）。
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// 变动金额：正数为充值，负数为消费。
    /// </summary>
    public decimal Delta { get; set; }

    /// <summary>
    /// 变动后的账户余额（CNY）。
    /// </summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// 变动类型文案。
    /// </summary>
    public string Type => Delta >= 0 ? "充值" : "消费";

    /// <summary>
    /// 变动金额的绝对值。
    /// </summary>
    public decimal Amount => Math.Abs(Delta);

    /// <summary>
    /// 界面展示用时间文本。
    /// </summary>
    public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// 界面展示用变动金额文本（充值带 +，消费带 -）。
    /// </summary>
    public string DeltaText
        => (Delta >= 0 ? "+" : string.Empty)
           + Delta.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>
    /// 界面展示用余额文本。
    /// </summary>
    public string BalanceText => Balance.ToString("F2", CultureInfo.InvariantCulture);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeepSeekBalance.Models;

namespace DeepSeekBalance.Services;

/// <summary>
/// 余额变动日志的读写服务。
/// 每个 API 一份日志文件，文件名取自该密钥的明文脱敏串，形如 "{脱敏串}.log.txt"，
/// 存放在插件日志目录（插件配置目录下的 Logs 文件夹）。
/// 日志永久保存、不做条数裁剪；写入采用"临时文件 + 原子替换"，避免中途异常导致文件损坏。
/// 该日志文件不加密，也不写入注册表。
/// </summary>
public class BalanceLogService
{
    /// <summary>
    /// 单行日志格式：[YYYY-MM-DD-hh-mm-ss] 脱敏串 已消费/充值 xx.xx元，当前余额 xx.xx元
    /// </summary>
    private static readonly Regex LinePattern = new(
        @"^\[(?<time>\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2})\]\s+(?<masked>\S+)\s+已(?<type>充值|消费)\s+(?<amount>\d+(?:\.\d+)?)\s*元，当前余额\s+(?<balance>-?\d+(?:\.\d+)?)\s*元$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _sync = new();

    /// <summary>
    /// 插件日志目录（插件配置目录\Logs）。
    /// </summary>
    public string FolderPath { get; }

    public BalanceLogService(string pluginConfigFolder)
    {
        FolderPath = Path.Combine(pluginConfigFolder, "Logs");
    }

    /// <summary>
    /// 获取指定脱敏串对应的日志文件完整路径。
    /// 脱敏串中的通配符等非法文件名字符会被替换为下划线。
    /// </summary>
    public string GetLogFilePath(string maskedKey)
        => Path.Combine(FolderPath, SanitizeFileName(maskedKey) + ".log.txt");

    /// <summary>
    /// 追加一条日志（原子写盘：写临时文件后整体替换）。
    /// </summary>
    public void Append(string maskedKey, BalanceLogEntry entry)
    {
        if (string.IsNullOrEmpty(maskedKey))
        {
            return;
        }

        var line = Format(maskedKey, entry);
        lock (_sync)
        {
            var path = GetLogFilePath(maskedKey);
            var lines = File.Exists(path)
                ? new List<string>(File.ReadAllLines(path))
                : new List<string>();
            lines.Add(line);
            AtomicWriteAllLines(path, lines);
        }
    }

    /// <summary>
    /// 读取指定 API 最近的若干条日志（按时间倒序，最新在前）。
    /// 仅读取文件尾部，避免整份大文件载入内存。
    /// </summary>
    public IReadOnlyList<BalanceLogEntry> ReadRecent(string maskedKey, int maxCount)
    {
        if (string.IsNullOrEmpty(maskedKey) || maxCount <= 0)
        {
            return Array.Empty<BalanceLogEntry>();
        }

        lock (_sync)
        {
            var path = GetLogFilePath(maskedKey);
            if (!File.Exists(path))
            {
                return Array.Empty<BalanceLogEntry>();
            }

            // 环形缓冲只保留末尾 maxCount 行。
            var buffer = new Queue<string>(maxCount);
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (buffer.Count == maxCount)
                {
                    buffer.Dequeue();
                }

                buffer.Enqueue(line);
            }

            var result = new List<BalanceLogEntry>(buffer.Count);
            foreach (var line in buffer)
            {
                if (TryParse(line, out var entry))
                {
                    result.Add(entry);
                }
            }

            result.Reverse();
            return result;
        }
    }

    private static string Format(string maskedKey, BalanceLogEntry entry)
        => string.Format(
            CultureInfo.InvariantCulture,
            "[{0:yyyy-MM-dd-HH-mm-ss}] {1} 已{2} {3:F2}元，当前余额 {4:F2}元",
            entry.Time, maskedKey, entry.Type, entry.Amount, entry.Balance);

    private static bool TryParse(string line, out BalanceLogEntry entry)
    {
        entry = null!;

        var match = LinePattern.Match(line);
        if (!match.Success)
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                match.Groups["time"].Value,
                "yyyy-MM-dd-HH-mm-ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            return false;
        }

        if (!decimal.TryParse(match.Groups["amount"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            !decimal.TryParse(match.Groups["balance"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance))
        {
            return false;
        }

        var delta = match.Groups["type"].Value == "充值" ? amount : -amount;
        entry = new BalanceLogEntry { Time = time, Delta = delta, Balance = balance };
        return true;
    }

    /// <summary>
    /// 原子写盘：先写同目录下的临时文件，再用其整体替换目标文件，
    /// 保证任意时刻异常退出都不会留下写了一半的日志文件。
    /// </summary>
    private static void AtomicWriteAllLines(string path, IReadOnlyList<string> lines)
    {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);

        var tempPath = Path.Combine(
            folder,
            Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            File.WriteAllLines(tempPath, lines);

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // 清理临时文件失败可忽略：下次写入会重新生成新的临时名。
                }
            }
        }
    }

    /// <summary>
    /// 把脱敏串转换为合法文件名（通配符等非法字符替换为下划线）。
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }
}

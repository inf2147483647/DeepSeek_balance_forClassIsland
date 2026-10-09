using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Shared.Helpers;
using DeepSeekBalance.Helpers;
using DeepSeekBalance.Models;
using Microsoft.Win32;

namespace DeepSeekBalance.Services;

/// <summary>
/// 管理 DeepSeek API 密钥列表，并负责余额查询与配置持久化。
/// 完整密钥与脱敏展示串均经 Windows DPAPI（当前用户范围）加密后保存在注册表
/// HKCU\Software\DeepSeekBalance\Apis 中，任何密钥相关材料都不以明文写入磁盘，
/// 避免调试/打包时密钥随文件泄露。
/// </summary>
public class DeepSeekService
{
    private const string BalanceUrl = "https://api.deepseek.com/user/balance";

    private const string RegistryRootKey = @"Software\DeepSeekBalance";
    private const string RegistryApisKey = "Apis";
    private const string RegistryBackupKey = "Backup";
    private const string ValueApiKey = "ApiKey";
    private const string ValueMaskedKey = "MaskedKey";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly string _legacyConfigPath;

    /// <summary>
    /// 串行化"校验+添加"流程，防止并发调用（如快速双击）绕过去重检查重复添加。
    /// </summary>
    private readonly SemaphoreSlim _addLock = new(1, 1);

    /// <summary>
    /// 当前插件设置。
    /// </summary>
    public PluginSettings Settings { get; }

    public DeepSeekService(string configFolder)
    {
        _legacyConfigPath = Path.Combine(configFolder, "Settings.json");
        Settings = new PluginSettings();

        // 加载顺序：
        //   1) 注册表主键 Apis（含旧版本写入的明文，将在后续重新加密）
        //   2) Apis 整体缺失（写入中途掉电等）时，从备份键 Backup 恢复
        //   3) 旧版 Settings.json 迁移、明文重新加密，并销毁旧文件
        var legacy = LoadLegacySettings();
        var hasPlaintextInRegistry = LoadFromRegistry();

        var recoveredFromBackup = false;
        if (Settings.Apis.Count == 0)
        {
            recoveredFromBackup = LoadFromBackup();
        }

        var legacyMigrated = MigrateLegacyIfNeeded(legacy);

        // 合并迁移已在内部执行过 Save；其余需要落盘的情形在此统一保存。
        if ((hasPlaintextInRegistry || recoveredFromBackup) && !legacyMigrated)
        {
            Save();
        }
    }

    /// <summary>
    /// 将当前密钥列表保存到注册表。
    /// 注册表不支持跨多个子键的事务或原子改名，因此采用等价原子语义的
    /// "合并 → 备份 → 剪枝"顺序，保证进程在任意时刻异常退出都不会丢失有效配置：
    /// <list type="number">
    /// <item>合并（upsert）：逐条创建/覆盖每个当前密钥子键的两个密文值。
    /// 此阶段结束后，所有需要保留的密钥都已存在且为最新；旧值被覆盖而非先删除。</item>
    /// <item>备份：把当前集合完整写入 Backup 键，供下次启动 Apis 整体缺失时恢复。</item>
    /// <item>剪枝（prune）：最后删除已不在列表中的陈旧子键。
    /// 此阶段崩溃最坏只遗留一条陈旧（仍有效加密）的密钥，不会丢失任何需要的密钥。</item>
    /// </list>
    /// </summary>
    public void Save()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var rootKey = Registry.CurrentUser.CreateSubKey(RegistryRootKey);

        // 阶段 1：合并——所有目标密钥先就位。
        using (var apisKey = rootKey.CreateSubKey(RegistryApisKey))
        {
            foreach (var api in Settings.Apis)
            {
                using var entryKey = apisKey.CreateSubKey(api.Id);
                entryKey.SetValue(ValueApiKey, SecretProtector.Protect(api.ApiKey), RegistryValueKind.String);
                // 脱敏串同样加密：注册表中不存在任何明文的密钥相关材料。
                entryKey.SetValue(ValueMaskedKey, SecretProtector.Protect(api.MaskedKey), RegistryValueKind.String);
            }
        }

        // 阶段 2：备份——最后一份完整的良好状态。
        ReplaceKey(rootKey, RegistryBackupKey,
            Settings.Apis.Select(api => new KeyValuePair<string, (string ApiKey, string MaskedKey)>(
                api.Id, (api.ApiKey, api.MaskedKey))));

        // 阶段 3：剪枝——全部目标数据已落盘后，才删除陈旧子键。
        using (var apisKey = rootKey.CreateSubKey(RegistryApisKey))
        {
            var currentIds = Settings.Apis.Select(x => x.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var name in apisKey.GetSubKeyNames())
            {
                if (!currentIds.Contains(name))
                {
                    apisKey.DeleteSubKeyTree(name, false);
                }
            }
        }
    }

    /// <summary>
    /// 用给定条目整体重建一个子键：先删除旧子键，再逐条写入加密值。
    /// 仅用于备份键等"可整体重建"的辅助键。
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ReplaceKey(
        RegistryKey parent,
        string keyName,
        IEnumerable<KeyValuePair<string, (string ApiKey, string MaskedKey)>> entries)
    {
        parent.DeleteSubKeyTree(keyName, false);
        using var container = parent.CreateSubKey(keyName);
        foreach (var (id, values) in entries)
        {
            using var entryKey = container.CreateSubKey(id);
            entryKey.SetValue(ValueApiKey, SecretProtector.Protect(values.ApiKey), RegistryValueKind.String);
            entryKey.SetValue(ValueMaskedKey, SecretProtector.Protect(values.MaskedKey), RegistryValueKind.String);
        }
    }

    /// <summary>
    /// 生成用于展示的脱敏密钥。
    /// </summary>
    public static string Mask(string key)
        => key.Length <= 10 ? key : $"{key[..6]}****{key[^4..]}";

    /// <summary>
    /// 校验密钥可用性，通过后添加到 API 列表。
    /// 整个"去重→联网校验→写入"过程在锁内串行执行，防止相同密钥被并发重复添加。
    /// </summary>
    public async Task<(bool Ok, string Message)> TryAddApiAsync(string key)
    {
        key = key.Trim();
        if (key.Length == 0)
        {
            return (false, "请输入 API 密钥。");
        }

        await _addLock.WaitAsync();
        try
        {
            // 锁内再次检查：可能已有前一个并发请求完成了添加。
            if (ContainsKey(key))
            {
                return (false, "该密钥已存在于列表中。");
            }

            var (ok, message, _) = await QueryAsync(key);
            if (!ok)
            {
                return (false, message);
            }

            // 网络校验结束后再确认一次（锁内串行下理论上不可能变化，作为兜底）。
            if (ContainsKey(key))
            {
                return (false, "该密钥已存在于列表中。");
            }

            Settings.Apis.Add(new DeepSeekApi
            {
                MaskedKey = Mask(key),
                ApiKey = key
            });
            Save();
            return (true, "验证通过，已添加到 API 列表。");
        }
        finally
        {
            _addLock.Release();
        }
    }

    /// <summary>
    /// 判断指定密钥是否已存在（大小写敏感的序号比较；DeepSeek 密钥区分大小写）。
    /// </summary>
    private bool ContainsKey(string key)
        => Settings.Apis.Any(api => api.ApiKey == key);

    /// <summary>
    /// 从列表中删除指定的 API。
    /// </summary>
    public void RemoveApi(DeepSeekApi api)
    {
        Settings.Apis.Remove(api);
        Save();
    }

    /// <summary>
    /// 获取指定 API 的余额（CNY）。失败时返回 null。
    /// </summary>
    public async Task<decimal?> GetBalanceAsync(DeepSeekApi api)
    {
        if (string.IsNullOrEmpty(api.ApiKey))
        {
            return null;
        }

        var (ok, _, balance) = await QueryAsync(api.ApiKey);
        return ok ? balance : null;
    }

    /// <summary>
    /// 调用 DeepSeek 余额查询接口。
    /// </summary>
    public async Task<(bool Ok, string Message, decimal? Balance)> QueryAsync(string key)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BalanceUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            using var response = await HttpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return (false, "API 密钥无效或已失效（HTTP 401）。", null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"请求失败：HTTP {(int)response.StatusCode}。", null);
            }

            var json = await response.Content.ReadAsStringAsync();
            var balance = ParseBalance(json);
            return balance is null
                ? (false, "无法解析返回的余额数据。", null)
                : (true, "验证通过。", balance);
        }
        catch (Exception ex)
        {
            return (false, $"请求出错：{ex.Message}", null);
        }
    }

    private static decimal? ParseBalance(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("balance_infos", out var infos) ||
                infos.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement? cny = null;
            JsonElement? first = null;
            foreach (var info in infos.EnumerateArray())
            {
                first ??= info;
                if (info.TryGetProperty("currency", out var currency) &&
                    string.Equals(currency.GetString(), "CNY", StringComparison.OrdinalIgnoreCase))
                {
                    cny = info;
                    break;
                }
            }

            var chosen = cny ?? first;
            if (chosen is null)
            {
                return null;
            }

            return chosen.Value.TryGetProperty("total_balance", out var total) &&
                   decimal.TryParse(total.GetString(), out var value)
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 读取旧版本保存在插件配置目录 Settings.json 中的密钥；文件不存在时返回 null。
    /// </summary>
    private PluginSettings? LoadLegacySettings()
    {
        if (!File.Exists(_legacyConfigPath))
        {
            return null;
        }

        var legacy = ConfigureFileHelper.LoadConfig<PluginSettings>(_legacyConfigPath);
        legacy.Apis ??= new ObservableCollection<DeepSeekApi>();
        return legacy;
    }

    /// <summary>
    /// 从注册表加载已保存的密钥并解密。
    /// 返回值表示是否发现旧版本写入的明文（需要重新加密写回）。
    /// </summary>
    private bool LoadFromRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var apisKey = Registry.CurrentUser.OpenSubKey(
                Path.Combine(RegistryRootKey, RegistryApisKey));
            if (apisKey is null)
            {
                return false;
            }

            var hasPlaintextEntry = false;
            foreach (var id in apisKey.GetSubKeyNames())
            {
                using var entryKey = apisKey.OpenSubKey(id);
                if (entryKey?.GetValue(ValueApiKey) is not string stored ||
                    string.IsNullOrEmpty(stored))
                {
                    continue;
                }

                var (apiKey, isEncrypted) = DecryptStored(stored);
                if (!isEncrypted)
                {
                    hasPlaintextEntry = true;
                }

                var storedMasked = entryKey.GetValue(ValueMaskedKey) as string;
                string maskedKey;
                if (string.IsNullOrEmpty(storedMasked))
                {
                    maskedKey = Mask(apiKey);
                }
                else
                {
                    // 新版本中脱敏串也是密文；旧版本的明文脱敏串解密会失败并原样返回。
                    var (decodedMasked, maskedEncrypted) = DecryptStored(storedMasked);
                    maskedKey = decodedMasked;
                    if (!maskedEncrypted)
                    {
                        hasPlaintextEntry = true;
                    }
                }

                Settings.Apis.Add(new DeepSeekApi
                {
                    Id = id,
                    ApiKey = apiKey,
                    MaskedKey = maskedKey
                });
            }

            return hasPlaintextEntry;
        }
        catch
        {
            // 注册表读取异常时以空列表启动，不阻断插件加载。
            return false;
        }
    }

    /// <summary>
    /// 从备份键恢复密钥到内存集合，并返回是否恢复出任意条目。
    /// 仅在 Apis 主键整体缺失时调用：备份内容与主键格式相同（密文）。
    /// </summary>
    private bool LoadFromBackup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var backupKey = Registry.CurrentUser.OpenSubKey(
                Path.Combine(RegistryRootKey, RegistryBackupKey));
            if (backupKey is null)
            {
                return false;
            }

            foreach (var id in backupKey.GetSubKeyNames())
            {
                using var entryKey = backupKey.OpenSubKey(id);
                if (entryKey?.GetValue(ValueApiKey) is not string stored ||
                    string.IsNullOrEmpty(stored))
                {
                    continue;
                }

                var (apiKey, _) = DecryptStored(stored);
                var (maskedKey, _) = entryKey.GetValue(ValueMaskedKey) is string storedMasked
                    ? DecryptStored(storedMasked)
                    : (Mask(apiKey), false);

                Settings.Apis.Add(new DeepSeekApi
                {
                    Id = id,
                    ApiKey = apiKey,
                    MaskedKey = maskedKey
                });
            }

            return Settings.Apis.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解密注册表中存储的值，并识别旧版本写入的明文：
    /// DeepSeek 完整密钥固定以 "sk-" 开头（DPAPI Base64 密文不会有此前缀），直接判为明文；
    /// 其余值尝试 DPAPI 解密，失败（明文脱敏串/损坏/换用户/换机器）时按原值返回。
    /// </summary>
    private static (string ApiKey, bool IsEncrypted) DecryptStored(string stored)
    {
        if (stored.StartsWith("sk-", StringComparison.Ordinal))
        {
            return (stored, false);
        }

        try
        {
            return (SecretProtector.Unprotect(stored), true);
        }
        catch
        {
            return (stored, false);
        }
    }

    /// <summary>
    /// 将旧 Settings.json 中的密钥合并进注册表，随后销毁磁盘上的旧密钥文件。
    /// 同时按 Id 与完整密钥去重：已存在的 Id 跳过，密钥相同但 Id 不同同样跳过，
    /// JSON 内部的重复条目也只保留一条，避免相同 API 重复导入。
    /// 返回值表示是否在本过程中执行了加密保存。
    /// </summary>
    private bool MigrateLegacyIfNeeded(PluginSettings? legacy)
    {
        if (legacy?.Apis is null || legacy.Apis.Count == 0)
        {
            return false;
        }

        var existingIds = Settings.Apis.Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
        var existingKeys = Settings.Apis.Select(x => x.ApiKey)
            .ToHashSet(StringComparer.Ordinal);
        var hasNewEntry = false;

        foreach (var old in legacy.Apis)
        {
            if (string.IsNullOrEmpty(old.ApiKey) ||
                existingIds.Contains(old.Id) ||
                existingKeys.Contains(old.ApiKey))
            {
                continue;
            }

            Settings.Apis.Add(old);
            existingIds.Add(old.Id);
            existingKeys.Add(old.ApiKey);
            hasNewEntry = true;
        }

        if (hasNewEntry)
        {
            Save();
        }

        ScrubLegacyConfigFile();
        return hasNewEntry;
    }

    /// <summary>
    /// 销毁旧的明文密钥文件：优先直接删除；删除失败时用空设置覆盖，确保密钥不残留。
    /// </summary>
    private void ScrubLegacyConfigFile()
    {
        try
        {
            if (File.Exists(_legacyConfigPath))
            {
                File.Delete(_legacyConfigPath);
            }
        }
        catch
        {
            try
            {
                ConfigureFileHelper.SaveConfig(_legacyConfigPath, new PluginSettings());
            }
            catch
            {
                // 文件被占用且无法覆盖时忽略，下次启动会再次尝试迁移清理。
            }
        }
    }
}

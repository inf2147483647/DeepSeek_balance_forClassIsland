using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using ClassIsland.Shared.Helpers;
using DeepSeekBalance.Helpers;
using DeepSeekBalance.Models;
using Microsoft.Win32;

namespace DeepSeekBalance.Services;

/// <summary>
/// 管理 DeepSeek API 密钥列表，并负责余额查询与配置持久化。
/// 密钥经 Windows DPAPI（当前用户范围）加密后保存在注册表
/// HKCU\Software\DeepSeekBalance\Apis 中，不以明文写入磁盘，
/// 避免调试/打包时密钥随文件泄露。
/// </summary>
public class DeepSeekService
{
    private const string BalanceUrl = "https://api.deepseek.com/user/balance";

    private const string RegistryRootKey = @"Software\DeepSeekBalance";
    private const string RegistryApisKey = "Apis";
    private const string ValueApiKey = "ApiKey";
    private const string ValueMaskedKey = "MaskedKey";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly string _legacyConfigPath;

    /// <summary>
    /// 当前插件设置。
    /// </summary>
    public PluginSettings Settings { get; }

    public DeepSeekService(string configFolder)
    {
        _legacyConfigPath = Path.Combine(configFolder, "Settings.json");
        Settings = new PluginSettings();

        // 先读取旧版本（升级前）落在配置目录中的明文密钥，
        // 再从注册表加载（含旧版本写入的明文），最后统一迁移：
        // 旧 JSON 密钥并入注册表、明文密钥重新加密写回，并销毁旧文件。
        var legacy = LoadLegacySettings();
        var hasPlaintextInRegistry = LoadFromRegistry();
        var legacyMigrated = MigrateLegacyIfNeeded(legacy);

        if (hasPlaintextInRegistry && !legacyMigrated)
        {
            Save();
        }
    }

    /// <summary>
    /// 将当前密钥列表写入注册表（全量同步）。
    /// </summary>
    public void Save()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var rootKey = Registry.CurrentUser.CreateSubKey(RegistryRootKey);
        using var apisKey = rootKey.CreateSubKey(RegistryApisKey);

        foreach (var name in apisKey.GetSubKeyNames())
        {
            apisKey.DeleteSubKeyTree(name, false);
        }

        foreach (var api in Settings.Apis)
        {
            using var entryKey = apisKey.CreateSubKey(api.Id);
            entryKey.SetValue(ValueApiKey, SecretProtector.Protect(api.ApiKey), RegistryValueKind.String);
            entryKey.SetValue(ValueMaskedKey, api.MaskedKey, RegistryValueKind.String);
        }
    }

    /// <summary>
    /// 生成用于展示的脱敏密钥。
    /// </summary>
    public static string Mask(string key)
        => key.Length <= 10 ? key : $"{key[..6]}****{key[^4..]}";

    /// <summary>
    /// 校验密钥可用性，通过后添加到 API 列表。
    /// </summary>
    public async Task<(bool Ok, string Message)> TryAddApiAsync(string key)
    {
        key = key.Trim();

        foreach (var api in Settings.Apis)
        {
            if (api.ApiKey == key)
            {
                return (false, "该密钥已存在于列表中。");
            }
        }

        var (ok, message, _) = await QueryAsync(key);
        if (!ok)
        {
            return (false, message);
        }

        Settings.Apis.Add(new DeepSeekApi
        {
            MaskedKey = Mask(key),
            ApiKey = key
        });
        Save();
        return (true, "验证通过，已添加到 API 列表。");
    }

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

                var maskedKey = entryKey.GetValue(ValueMaskedKey) as string;
                Settings.Apis.Add(new DeepSeekApi
                {
                    Id = id,
                    ApiKey = apiKey,
                    MaskedKey = string.IsNullOrEmpty(maskedKey) ? Mask(apiKey) : maskedKey
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
    /// 解密注册表中存储的密钥，并识别旧版本写入的明文：
    /// DeepSeek 密钥固定以 "sk-" 开头（密文为 DPAPI Base64，不会有此前缀）；
    /// 非 sk- 开头但解密失败（损坏/换用户/换机器）时也按明文原值返回。
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
    /// 将旧 Settings.json 中的密钥按 Id 合并进注册表（已存在的 Id 不重复导入），
    /// 随后销毁磁盘上的旧密钥文件。返回值表示是否在本过程中执行了加密保存。
    /// </summary>
    private bool MigrateLegacyIfNeeded(PluginSettings? legacy)
    {
        if (legacy?.Apis is null || legacy.Apis.Count == 0)
        {
            return false;
        }

        var existingIds = Settings.Apis.Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
        var hasNewEntry = false;

        foreach (var old in legacy.Apis)
        {
            if (string.IsNullOrEmpty(old.ApiKey) || existingIds.Contains(old.Id))
            {
                continue;
            }

            Settings.Apis.Add(old);
            existingIds.Add(old.Id);
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

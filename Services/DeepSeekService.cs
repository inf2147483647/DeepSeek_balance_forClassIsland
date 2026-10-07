﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using ClassIsland.Shared.Helpers;
using DeepSeekBalance.Models;

namespace DeepSeekBalance.Services;

/// <summary>
/// 管理 DeepSeek API 密钥列表，并负责余额查询与配置持久化。
/// </summary>
public class DeepSeekService
{
    private const string BalanceUrl = "https://api.deepseek.com/user/balance";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly string _configPath;

    /// <summary>
    /// 当前插件设置。
    /// </summary>
    public PluginSettings Settings { get; }

    public DeepSeekService(string configFolder)
    {
        _configPath = Path.Combine(configFolder, "Settings.json");
        Settings = ConfigureFileHelper.LoadConfig<PluginSettings>(_configPath);
        Settings.Apis ??= new ObservableCollection<DeepSeekApi>();
    }

    /// <summary>
    /// 将当前设置写入配置文件。
    /// </summary>
    public void Save() => ConfigureFileHelper.SaveConfig(_configPath, Settings);

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
}

using System.Collections.ObjectModel;

namespace DeepSeekBalance.Models;

/// <summary>
/// 插件密钥集合（内存容器，运行时由 <see cref="DeepSeekBalance.Services.DeepSeekService"/> 维护）。
/// 密钥经 Windows DPAPI 加密后持久化到当前用户注册表，而非本类型对应的 JSON 文件；
/// 本类型仍用于读取/迁移旧版本保存在 Settings.json 中的密钥。
/// </summary>
public class PluginSettings
{
    /// <summary>
    /// 已配置的 DeepSeek API 密钥列表。
    /// </summary>
    public ObservableCollection<DeepSeekApi> Apis { get; set; } = new();
}

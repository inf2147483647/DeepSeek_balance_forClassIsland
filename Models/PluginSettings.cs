﻿using System.Collections.ObjectModel;

namespace DeepSeekBalance.Models;

/// <summary>
/// 插件设置，持久化到插件配置目录下的 Settings.json。
/// </summary>
public class PluginSettings
{
    /// <summary>
    /// 已配置的 DeepSeek API 密钥列表。
    /// </summary>
    public ObservableCollection<DeepSeekApi> Apis { get; set; } = new();
}

﻿using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using DeepSeekBalance.Components;
using DeepSeekBalance.Services;
using DeepSeekBalance.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DeepSeekBalance;

[PluginEntrance]
public class Plugin : PluginBase
{
    /// <summary>
    /// 当前插件实例。界面控件通过它访问插件级服务。
    /// </summary>
    public static Plugin Instance { get; private set; } = null!;

    /// <summary>
    /// DeepSeek API 密钥与余额服务。
    /// </summary>
    public DeepSeekService Service { get; private set; } = null!;

    public Plugin()
    {
        Instance = this;
    }

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        Service = new DeepSeekService(PluginConfigFolder);
        services.AddSingleton(Service);

        services.AddComponent<DeepSeekBalanceComponent, DeepSeekComponentSettingsControl>();
        services.AddSettingsPage<DeepSeekPluginSettingsPage>();
    }
}

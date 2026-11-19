using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Platforms.Abstraction;
using DeepSeekBalance.Helpers;
using DeepSeekBalance.Models;

namespace DeepSeekBalance.Services.NotificationProviders;

/// <summary>
/// 余额阈值预警的提醒提供方：
/// 周期性检查所有已配置 API 的余额，不高于阈值时同时发送 ClassIsland 提醒
/// 与 Windows 系统通知（Win10+ 使用 toast，Win8.1- 回退托盘气泡）。
/// </summary>
[NotificationProviderInfo(
    "C4E1A6B2-7D93-4F0A-8B5C-1E2D3F4A6B70",
    "DeepSeek 余额预警",
    "\uF550",
    "当 DeepSeek API 余额不高于设定阈值时，通过 ClassIsland 与 Windows 通知发出预警。")]
[NotificationChannelInfo(
    ChannelId,
    "DeepSeek 余额预警",
    "\uF550",
    "DeepSeek API 余额不足时发送遮罩与正文提醒。")]
public class DeepSeekAlertNotificationProvider : NotificationProviderBase<DeepSeekAlertNotificationProviderSettings>
{
    /// <summary>
    /// 余额预警提醒渠道 GUID。
    /// </summary>
    public const string ChannelId = "C4E1A6B2-7D93-4F0A-8B5C-1E2D3F4A6B71";

    /// <summary>
    /// 余额检查间隔。组件本身按 5 秒刷新，但只刷新所选 API；
    /// 预警需要覆盖全部 API，故使用更宽松的独立轮询。
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private const string DefaultMaskText = "API余额不足预警";
    private const string DefaultOverlayText = "您的 DeepSeek API 余额不足<阈值>元，为避免影响使用，请尽快充值。";

    private readonly DeepSeekService _service;

    /// <summary>
    /// 已发送过预警的 API Id 集合：余额回到阈值以上后才允许再次预警，
    /// 避免余额持续低于阈值时每个轮询周期重复打扰。
    /// </summary>
    private readonly HashSet<string> _alertedApiIds = new();

    private readonly object _alertedLock = new();

    /// <summary>
    /// 存活的提供方实例（弱引用），供设置页“测试提醒”调用，避免静态强引用导致实例无法释放。
    /// </summary>
    private static readonly object ProvidersLock = new();
    private static readonly List<WeakReference<DeepSeekAlertNotificationProvider>> Providers = new();

    private bool _monitorStarted;

    public DeepSeekAlertNotificationProvider(DeepSeekService service)
    {
        _service = service;

        lock (ProvidersLock)
        {
            Providers.Add(new WeakReference<DeepSeekAlertNotificationProvider>(this));
        }

        StartMonitor();
    }

    /// <summary>
    /// 使用当前预警阈值与当前提醒文案立即发送一次测试提醒，不受预警开关与阈值判断影响。
    /// 测试仅调用无状态的 <see cref="Notify"/>，不会读写“已提醒”状态与余额缓存，
    /// 因此不会影响正式预警的触发时机与次数。
    /// </summary>
    public static void TriggerTest()
    {
        List<DeepSeekAlertNotificationProvider> alive = new();
        lock (ProvidersLock)
        {
            for (var i = Providers.Count - 1; i >= 0; i--)
            {
                if (Providers[i].TryGetTarget(out var provider))
                {
                    alive.Add(provider);
                }
                else
                {
                    Providers.RemoveAt(i);
                }
            }
        }

        foreach (var provider in alive)
        {
            provider.Notify(provider._service.AlertSettings.Threshold);
        }
    }

    /// <summary>
    /// 提醒提供方基类不提供可重写的 StartAsync，故在构造后启动后台轮询。
    /// 轮询线程为后台线程，随宿主进程退出而结束。
    /// </summary>
    private void StartMonitor()
    {
        if (_monitorStarted)
        {
            return;
        }

        _monitorStarted = true;
        _ = Task.Run(MonitorLoopAsync);
    }

    private async Task MonitorLoopAsync()
    {
        // 等待宿主与插件服务就绪后再开始查询。
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // 忽略取消异常。
        }

        using var timer = new PeriodicTimer(PollInterval);
        while (true)
        {
            try
            {
                await CheckBalancesAsync();
            }
            catch
            {
                // 单次检查失败不影响后续轮询。
            }

            try
            {
                if (!await timer.WaitForNextTickAsync())
                {
                    return;
                }
            }
            catch
            {
                return;
            }
        }
    }

    private async Task CheckBalancesAsync()
    {
        var alert = _service.AlertSettings;
        if (!alert.IsEnabled)
        {
            // 关闭预警后清空状态：重新开启时若余额仍不足会再次提醒。
            lock (_alertedLock)
            {
                _alertedApiIds.Clear();
            }

            return;
        }

        // 未选择任何提醒方式时不消耗“只提醒一次”的状态，待用户勾选提醒方式后仍能提醒。
        if (!alert.NotifyViaWindows && !alert.NotifyViaClassIsland)
        {
            return;
        }

        var threshold = alert.Threshold;
        foreach (var api in _service.Settings.Apis.ToList())
        {
            var balance = await _service.GetBalanceAsync(api);
            if (balance is null)
            {
                continue;
            }

            var isLow = balance.Value <= threshold;
            bool shouldNotify;
            lock (_alertedLock)
            {
                shouldNotify = isLow
                    ? _alertedApiIds.Add(api.Id)
                    : _alertedApiIds.Remove(api.Id);
            }

            if (shouldNotify)
            {
                Notify(threshold);
            }
        }
    }

    /// <summary>
    /// 纯展示逻辑：只读取设置并发送提醒，不修改任何预警状态（“已提醒”集合、余额缓存、阈值等）。
    /// 正式预警与测试提醒共用该方法。
    /// </summary>
    private void Notify(decimal threshold)
    {
        var settings = Settings;
        var maskText = string.IsNullOrWhiteSpace(settings?.MaskText) ? DefaultMaskText : settings!.MaskText;
        var overlayText = BuildOverlayText(settings?.OverlayText, threshold);
        var maskDuration = TimeSpan.FromSeconds(Math.Max(1, settings?.MaskDurationSeconds ?? 10));
        var overlayDuration = TimeSpan.FromSeconds(Math.Max(1, settings?.OverlayDurationSeconds ?? 10));
        var alert = _service.AlertSettings;

        // ClassIsland 提醒（遮罩 + 正文）。
        if (alert.NotifyViaClassIsland)
        {
            try
            {
                var request = new NotificationRequest
                {
                    MaskContent = NotificationContent.CreateTwoIconsMask(maskText, factory: x =>
                    {
                        x.Duration = maskDuration;
                    }),
                    OverlayContent = NotificationContent.CreateSimpleTextContent(overlayText, x =>
                    {
                        x.Duration = overlayDuration;
                    })
                };

                Channel(ChannelId).ShowNotification(request);
            }
            catch
            {
                // 提醒发送失败不阻断系统通知，也不重试。
            }
        }

        // Windows 系统通知。
        if (alert.NotifyViaWindows)
        {
            ShowWindowsNotification(maskText, overlayText);
        }
    }

    /// <summary>
    /// 将文案中的 &lt;阈值&gt;（或 {0}）替换为当前阈值，保留两位小数。
    /// </summary>
    private static string BuildOverlayText(string? template, decimal threshold)
    {
        var text = string.IsNullOrWhiteSpace(template) ? DefaultOverlayText : template!;
        var value = threshold.ToString("0.00", CultureInfo.InvariantCulture);
        return text.Replace("<阈值>", value).Replace("{0}", value);
    }

    private static void ShowWindowsNotification(string title, string body)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Windows 10 及以上使用宿主平台的桌面 toast；Win8.1- 回退为托盘气泡。
        if (Environment.OSVersion.Version.Major >= 10)
        {
            try
            {
                _ = PlatformServices.DesktopToastService.ShowToastAsync(title, body);
                return;
            }
            catch
            {
                // toast 不可用时继续尝试气泡兜底。
            }
        }

        WindowsBalloonNotifier.Show(title, body);
    }
}

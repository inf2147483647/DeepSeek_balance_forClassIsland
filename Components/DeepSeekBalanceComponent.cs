﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using DeepSeekBalance.Helpers;
using DeepSeekBalance.Models;
using DeepSeekBalance.Services;

namespace DeepSeekBalance.Components;

/// <summary>
/// DeepSeek 余额主界面组件。
/// </summary>
[ComponentInfo(
    "8F2A1C33-6B47-4D92-9E10-5C7A3D4B6E21",
    "DeepSeek余额",
    "\uF550",
    "显示 DeepSeek API 账户余额，每 0.5 秒刷新一次。余额减少时闪红，充值时闪绿。")]
public class DeepSeekBalanceComponent : ComponentBase<DeepSeekComponentSettings>
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan FlashInterval = TimeSpan.FromMilliseconds(200);
    private static readonly IBrush DecreaseBrush = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly IBrush IncreaseBrush = new SolidColorBrush(Color.Parse("#43A047"));

    private readonly TextBlock _balanceText;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _flashTimer;

    private decimal? _lastBalance;
    private bool _isRefreshing;
    private IBrush? _flashBrush;
    private int _flashTicks;
    private bool _themeSubscribed;

    public DeepSeekBalanceComponent()
    {
        _balanceText = new TextBlock
        {
            Text = "余额：--CNY",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.GetTextBrush()
        };
        Content = _balanceText;

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += OnRefreshTick;

        _flashTimer = new DispatcherTimer { Interval = FlashInterval };
        _flashTimer.Tick += OnFlashTick;

        AttachedToVisualTree += (_, _) => OnAttached();
        DetachedFromVisualTree += (_, _) => OnDetached();
    }

    private static DeepSeekService? Service => Plugin.Instance?.Service;

    private void OnAttached()
    {
        SubscribeTheme();
        _refreshTimer.Start();
    }

    private void OnDetached()
    {
        _refreshTimer.Stop();
        _flashTimer.Stop();
        _flashBrush = null;
        ApplyFlash(null);
        UnsubscribeTheme();
    }

    private void SubscribeTheme()
    {
        if (_themeSubscribed || Application.Current is null)
        {
            return;
        }

        Application.Current.ActualThemeVariantChanged += OnThemeVariantChanged;
        _themeSubscribed = true;
    }

    private void UnsubscribeTheme()
    {
        if (!_themeSubscribed || Application.Current is null)
        {
            return;
        }

        Application.Current.ActualThemeVariantChanged -= OnThemeVariantChanged;
        _themeSubscribed = false;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        if (_flashBrush is null)
        {
            _balanceText.Foreground = ThemeHelper.GetTextBrush();
        }
    }

    private async void OnRefreshTick(object? sender, EventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // 忽略刷新过程中的异常，避免中断计时器。
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task RefreshAsync()
    {
        var service = Service;
        if (service is null)
        {
            return;
        }

        var selectedId = Settings?.SelectedApiId;
        var api = string.IsNullOrEmpty(selectedId)
            ? null
            : service.Settings.Apis.FirstOrDefault(x => x.Id == selectedId);

        if (api is null)
        {
            _lastBalance = null;
            _balanceText.Text = "余额：--CNY";
            return;
        }

        var balance = await service.GetBalanceAsync(api);
        if (balance is null)
        {
            // 查询失败时保留上一次的显示结果，避免网络抖动导致界面反复跳动。
            return;
        }

        var previous = _lastBalance;
        _lastBalance = balance;
        _balanceText.Text = $"余额：{balance.Value:F2}CNY";

        if (previous.HasValue && balance.Value != previous.Value)
        {
            StartFlash(balance.Value < previous.Value ? DecreaseBrush : IncreaseBrush);
        }
    }

    private void StartFlash(IBrush brush)
    {
        _flashBrush = brush;
        _flashTicks = 0;
        ApplyFlash(brush);
        _flashTimer.Start();
    }

    private void OnFlashTick(object? sender, EventArgs e)
    {
        _flashTicks++;
        if (_flashTicks >= 6)
        {
            _flashTimer.Stop();
            _flashBrush = null;
            ApplyFlash(null);
            return;
        }

        ApplyFlash(_flashTicks % 2 == 0 ? _flashBrush : null);
    }

    /// <summary>
    /// 应用闪烁颜色。传入 null 时恢复为随主题变化的默认前景色。
    /// </summary>
    private void ApplyFlash(IBrush? brush)
    {
        _balanceText.Foreground = brush ?? ThemeHelper.GetTextBrush();
    }
}

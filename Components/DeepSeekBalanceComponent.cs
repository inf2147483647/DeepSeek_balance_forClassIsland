using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
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
    "",
    "显示 DeepSeek API 账户余额，每 5 秒刷新一次，并按北京时间标注峰/谷计价时段。余额减少时闪红，充值时闪绿，随后以 0.5 秒过渡动画恢复原色。")]
public class DeepSeekBalanceComponent : ComponentBase<DeepSeekComponentSettings>
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>峰谷标签的独立刷新间隔。</summary>
    private static readonly TimeSpan PeriodLabelInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ColorTransitionDuration = TimeSpan.FromSeconds(0.5);
    private static readonly IBrush DecreaseBrush = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly IBrush IncreaseBrush = new SolidColorBrush(Color.Parse("#43A047"));

    private readonly TextBlock _balanceText;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _periodLabelTimer;
    private readonly Transitions _foregroundTransitions;

    private decimal? _lastBalance;

    /// <summary>当前展示的余额数字部分，如 "--CNY" 或 "12.34CNY"。</summary>
    private string _amountText = "--CNY";

    private bool _isRefreshing;
    private bool _themeSubscribed;

    public DeepSeekBalanceComponent()
    {
        // Foreground 的过渡动画：余额变化后由红/绿恢复为主题原色时，
        // 由 Avalonia 在 0.5 秒内完成颜色插值（非阻塞，不占用 UI 线程）。
        _foregroundTransitions = new Transitions
        {
            new BrushTransition
            {
                Property = TextBlock.ForegroundProperty,
                Duration = ColorTransitionDuration
            }
        };

        _balanceText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.GetTextBrush()
        };
        RenderBalanceText();
        Content = _balanceText;

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += OnRefreshTick;

        // 峰谷标签独立于余额刷新：时段切换只取决于时间，
        // 不应因余额查询失败或请求耗时而停滞在上一个标签。
        _periodLabelTimer = new DispatcherTimer { Interval = PeriodLabelInterval };
        _periodLabelTimer.Tick += OnPeriodLabelTick;

        AttachedToVisualTree += (_, _) => OnAttached();
        DetachedFromVisualTree += (_, _) => OnDetached();
    }

    private static DeepSeekService? Service => Plugin.Instance?.Service;

    /// <summary>
    /// 按当前余额数字与峰谷时段拼装完整展示文本，例如“余额：12.34CNY 峰”。
    /// </summary>
    private string BuildBalanceText()
        => $"余额：{_amountText} {PricingPeriodHelper.GetLabel(PricingPeriodHelper.GetBeijingNow())}";

    /// <summary>
    /// 重新渲染余额文本；内容未变化时不写入，避免每秒产生无谓的 UI 更新。
    /// </summary>
    private void RenderBalanceText()
    {
        var text = BuildBalanceText();
        if (text != _balanceText.Text)
        {
            _balanceText.Text = text;
        }
    }

    private void OnAttached()
    {
        SubscribeTheme();
        RenderBalanceText();
        _refreshTimer.Start();
        _periodLabelTimer.Start();
        // 立即刷新一次，避免挂载后等待一个刷新周期才显示余额。
        _ = RefreshSafeAsync();
    }

    private void OnDetached()
    {
        _refreshTimer.Stop();
        _periodLabelTimer.Stop();
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
        _balanceText.Foreground = ThemeHelper.GetTextBrush();
    }

    private async void OnRefreshTick(object? sender, EventArgs e) => await RefreshSafeAsync();

    /// <summary>
    /// 每秒检查峰谷时段是否切换，切换时才更新文本。
    /// </summary>
    private void OnPeriodLabelTick(object? sender, EventArgs e) => RenderBalanceText();

    private async Task RefreshSafeAsync()
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
            _amountText = "--CNY";
            RenderBalanceText();
            return;
        }

        var balance = await service.GetBalanceAsync(api);
        if (balance is null)
        {
            // 查询失败时保留上一次的余额数字，但峰谷标签仍需按当前时间刷新。
            RenderBalanceText();
            return;
        }

        var previous = _lastBalance;
        _lastBalance = balance;
        _amountText = $"{balance.Value:F2}CNY";
        RenderBalanceText();

        if (previous.HasValue && balance.Value != previous.Value)
        {
            FlashAndRecover(balance.Value < previous.Value ? DecreaseBrush : IncreaseBrush);
        }
    }

    /// <summary>
    /// 文本颜色瞬间变为红/绿，随后以 0.5 秒过渡动画恢复为随主题变化的默认前景色。
    /// </summary>
    private void FlashAndRecover(IBrush flashBrush)
    {
        // 暂时停用过渡，让颜色瞬间切换为红/绿。
        _balanceText.Transitions = null;
        _balanceText.Foreground = flashBrush;

        // 下一帧重新启用过渡并写回原色：Avalonia 会从当前颜色（红/绿）
        // 在 0.5 秒内插值到主题原色，动画由渲染系统驱动，不阻塞 UI。
        Dispatcher.UIThread.Post(() =>
        {
            _balanceText.Transitions = _foregroundTransitions;
            _balanceText.Foreground = ThemeHelper.GetTextBrush();
        });
    }
}

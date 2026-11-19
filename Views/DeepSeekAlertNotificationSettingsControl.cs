using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using DeepSeekBalance.Helpers;
using DeepSeekBalance.Models;
using DeepSeekBalance.Services.NotificationProviders;

namespace DeepSeekBalance.Views;

/// <summary>
/// 余额预警提醒提供方的设置控件：调整遮罩/正文的时长与文案。
/// 设置对象由 ClassIsland 随应用设置持久化，控件在附加到可视树后读写该对象。
/// </summary>
public class DeepSeekAlertNotificationSettingsControl : NotificationProviderControlBase<DeepSeekAlertNotificationProviderSettings>
{
    private readonly TextBlock _header;
    private readonly TextBlock _description;
    private readonly TextBlock _maskTextLabel;
    private readonly TextBlock _maskDurationLabel;
    private readonly TextBlock _overlayTextLabel;
    private readonly TextBlock _overlayDurationLabel;
    private readonly TextBlock _hint;
    private readonly Button _testButton;
    private readonly TextBlock _testStatus;
    private readonly TextBox _maskTextBox;
    private readonly TextBox _overlayTextBox;
    private readonly NumericUpDown _maskDurationBox;
    private readonly NumericUpDown _overlayDurationBox;
    private bool _themeSubscribed;
    private bool _loading;

    public DeepSeekAlertNotificationSettingsControl()
    {
        _header = new TextBlock
        {
            Text = "余额预警提醒",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = ThemeHelper.GetTextBrush()
        };

        _description = new TextBlock
        {
            Text = "当 DeepSeek API 余额不高于预警阈值时，通过 ClassIsland 提醒与 Windows 系统通知提示。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        _maskTextLabel = CreateLabel("遮罩标题文案");
        _maskTextBox = new TextBox { TextWrapping = TextWrapping.Wrap };
        _maskTextBox.TextChanged += (_, _) => Apply();

        _maskDurationLabel = CreateLabel("遮罩时长（秒）");
        _maskDurationBox = CreateDurationBox();
        _maskDurationBox.ValueChanged += (_, _) => Apply();

        _overlayTextLabel = CreateLabel("正文内容文案");
        _overlayTextBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72
        };
        _overlayTextBox.TextChanged += (_, _) => Apply();

        _overlayDurationLabel = CreateLabel("正文时长（秒）");
        _overlayDurationBox = CreateDurationBox();
        _overlayDurationBox.ValueChanged += (_, _) => Apply();

        _hint = new TextBlock
        {
            Text = "正文中的 <阈值>（或 {0}）会在发送时替换为当前预警阈值。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        _testButton = new Button
        {
            Content = "测试提醒",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _testButton.Click += OnTestClick;

        _testStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ThemeHelper.GetSubTextBrush(),
            IsVisible = false
        };

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 8)
        };
        root.Children.Add(_header);
        root.Children.Add(_description);
        root.Children.Add(_maskTextLabel);
        root.Children.Add(_maskTextBox);
        root.Children.Add(_maskDurationLabel);
        root.Children.Add(_maskDurationBox);
        root.Children.Add(_overlayTextLabel);
        root.Children.Add(_overlayTextBox);
        root.Children.Add(_overlayDurationLabel);
        root.Children.Add(_overlayDurationBox);
        root.Children.Add(_hint);
        root.Children.Add(_testButton);
        root.Children.Add(_testStatus);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };

        AttachedToVisualTree += (_, _) => OnAttached();
        DetachedFromVisualTree += (_, _) => UnsubscribeTheme();
    }

    private static TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 4, 0, 0),
        Foreground = ThemeHelper.GetTextBrush()
    };

    private static NumericUpDown CreateDurationBox() => new()
    {
        Minimum = 1,
        Maximum = 3600,
        Increment = 1,
        FormatString = "0",
        Value = 10,
        HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 140
    };

    private void OnAttached()
    {
        LoadSettings();
        SubscribeTheme();
    }

    /// <summary>
    /// 从设置对象载入控件当前值（Settings 在控件构造完成后才由框架注入）。
    /// </summary>
    private void LoadSettings()
    {
        var settings = Settings;
        if (settings is null)
        {
            return;
        }

        _loading = true;
        try
        {
            _maskTextBox.Text = settings.MaskText;
            _overlayTextBox.Text = settings.OverlayText;
            _maskDurationBox.Value = Math.Clamp(settings.MaskDurationSeconds, 1, 3600);
            _overlayDurationBox.Value = Math.Clamp(settings.OverlayDurationSeconds, 1, 3600);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// 将控件当前值写回设置对象。
    /// </summary>
    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        var settings = Settings;
        if (settings is null)
        {
            return;
        }

        settings.MaskText = _maskTextBox.Text ?? string.Empty;
        settings.OverlayText = _overlayTextBox.Text ?? string.Empty;
        settings.MaskDurationSeconds = (int)(_maskDurationBox.Value ?? 10);
        settings.OverlayDurationSeconds = (int)(_overlayDurationBox.Value ?? 10);
        OnPropertyChanged(nameof(Settings));
    }

    private void OnTestClick(object? sender, RoutedEventArgs e)
    {
        var alert = Plugin.Instance?.Service?.AlertSettings;
        if (alert is null)
        {
            return;
        }

        if (!alert.NotifyViaWindows && !alert.NotifyViaClassIsland)
        {
            _testStatus.Text = "当前未选择任何提醒方式，请先在主设置的“提醒方式”中勾选。";
            _testStatus.IsVisible = true;
            return;
        }

        DeepSeekAlertNotificationProvider.TriggerTest();
        _testStatus.Text = "已发送测试提醒。";
        _testStatus.IsVisible = true;
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
        _header.Foreground = ThemeHelper.GetTextBrush();
        _description.Foreground = ThemeHelper.GetSubTextBrush();
        _maskTextLabel.Foreground = ThemeHelper.GetTextBrush();
        _maskDurationLabel.Foreground = ThemeHelper.GetTextBrush();
        _overlayTextLabel.Foreground = ThemeHelper.GetTextBrush();
        _overlayDurationLabel.Foreground = ThemeHelper.GetTextBrush();
        _hint.Foreground = ThemeHelper.GetSubTextBrush();
        _testStatus.Foreground = ThemeHelper.GetSubTextBrush();
    }
}

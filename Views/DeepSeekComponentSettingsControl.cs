﻿using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using DeepSeekBalance.Helpers;
using DeepSeekBalance.Models;

namespace DeepSeekBalance.Views;

/// <summary>
/// DeepSeek 余额组件设置控件：选择要展示的 API。
/// </summary>
public class DeepSeekComponentSettingsControl : ComponentBase<DeepSeekComponentSettings>
{
    private readonly TextBlock _header;
    private readonly TextBlock _description;
    private readonly TextBlock _emptyHint;
    private readonly ComboBox _apiComboBox;
    private bool _themeSubscribed;

    public DeepSeekComponentSettingsControl()
    {
        _header = new TextBlock
        {
            Text = "要展示的 API",
            FontWeight = FontWeight.Bold,
            Foreground = ThemeHelper.GetTextBrush()
        };

        _description = new TextBlock
        {
            Text = "选择要在主界面组件上展示余额的 DeepSeek API。API 密钥可在应用设置 - DeepSeek 余额 中配置。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        _apiComboBox = new ComboBox
        {
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemTemplate = new FuncDataTemplate<DeepSeekApi>((model, _) =>
                new TextBlock { Text = model?.MaskedKey ?? string.Empty })
        };
        _apiComboBox.SelectionChanged += OnApiSelectionChanged;

        _emptyHint = new TextBlock
        {
            Text = "尚未配置任何 API，请先在应用设置 - DeepSeek 余额 中添加。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ThemeHelper.GetSubTextBrush(),
            IsVisible = false
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 4)
        };
        panel.Children.Add(_header);
        panel.Children.Add(_description);
        panel.Children.Add(_apiComboBox);
        panel.Children.Add(_emptyHint);
        Content = panel;

        AttachedToVisualTree += (_, _) => SubscribeTheme();
        DetachedFromVisualTree += (_, _) => UnsubscribeTheme();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        LoadApis();
    }

    private void LoadApis()
    {
        var service = Plugin.Instance?.Service;
        if (service is null)
        {
            return;
        }

        var apis = service.Settings.Apis;
        _apiComboBox.ItemsSource = apis;

        var selectedId = Settings?.SelectedApiId;
        _apiComboBox.SelectedItem = string.IsNullOrEmpty(selectedId)
            ? null
            : FindApi(selectedId);

        _emptyHint.IsVisible = apis.Count == 0;
    }

    private DeepSeekApi? FindApi(string id)
    {
        var service = Plugin.Instance?.Service;
        if (service is null)
        {
            return null;
        }

        foreach (var api in service.Settings.Apis)
        {
            if (api.Id == id)
            {
                return api;
            }
        }

        return null;
    }

    private void OnApiSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Settings is null)
        {
            return;
        }

        Settings.SelectedApiId = (_apiComboBox.SelectedItem as DeepSeekApi)?.Id ?? string.Empty;
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
        _emptyHint.Foreground = ThemeHelper.GetSubTextBrush();
    }
}

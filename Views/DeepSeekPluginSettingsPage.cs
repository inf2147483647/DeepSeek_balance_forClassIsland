using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using DeepSeekBalance.Helpers;
using DeepSeekBalance.Models;
using DeepSeekBalance.Services;

namespace DeepSeekBalance.Views;

/// <summary>
/// 插件设置页面：管理 DeepSeek API 密钥列表。
/// </summary>
[SettingsPageInfo("deepseekbalance.settings", "DeepSeek 余额", "\uF550", "\uF550")]
public class DeepSeekPluginSettingsPage : SettingsPageBase
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#43A047"));

    /// <summary>
    /// 设置页日志区最多展示的条数（完整日志在日志文件中）。
    /// </summary>
    private const int MaxLogDisplayCount = 500;

    /// <summary>
    /// 日志表格列定义：时间 | 类型 | 金额 | 余额。
    /// </summary>
    private const string LogColumnDefinitions = "Auto,70,*,*";

    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly TextBox _apiKeyTextBox;
    private readonly Button _addButton;
    private readonly TextBlock _statusText;
    private readonly TextBlock _listHeader;
    private readonly Border _tableBorder;
    private readonly Grid _tableHeader;
    private readonly TextBlock _keyColumnHeader;
    private readonly TextBlock _statusColumnHeader;
    private readonly ListBox _apiListBox;
    private readonly Button _deleteButton;
    private readonly TextBlock _deleteHint;
    private readonly TextBlock _footerHint;
    private readonly TextBlock _emptyHint;
    private readonly TextBlock _logHeader;
    private readonly ComboBox _logApiComboBox;
    private readonly Button _logRefreshButton;
    private readonly TextBlock _logTimeColumnHeader;
    private readonly TextBlock _logTypeColumnHeader;
    private readonly TextBlock _logAmountColumnHeader;
    private readonly TextBlock _logBalanceColumnHeader;
    private readonly Grid _logTableHeader;
    private readonly Border _logTableBorder;
    private readonly ListBox _logListBox;
    private readonly TextBlock _logEmptyHint;
    private readonly TextBlock _logFolderHint;
    private readonly TextBlock _alertHeader;
    private readonly TextBlock _alertDescription;
    private readonly TextBlock _alertLabel;
    private readonly TextBlock _alertHint;
    private readonly NumericUpDown _alertThresholdBox;
    private readonly ToggleSwitch _alertToggle;
    private readonly TextBlock _notifyMethodLabel;
    private readonly CheckBox _windowsNotifyCheckBox;
    private readonly CheckBox _classIslandNotifyCheckBox;
    private bool _loadingAlertSettings;
    private bool _themeSubscribed;

    public DeepSeekPluginSettingsPage()
    {
        _title = new TextBlock
        {
            Text = "DeepSeek API 密钥",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = ThemeHelper.GetTextBrush()
        };

        _description = new TextBlock
        {
            Text = "仅支持 DeepSeek 平台（密钥以 sk- 开头）。添加时会自动校验密钥是否可用，通过后才会加入下方的 API 列表。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        _apiKeyTextBox = new TextBox
        {
            Width = 300,
            PasswordChar = '●',
#if NET10_0
            // Avalonia 12（net10/CI 2.1.1.1）将 Watermark 重命名为 PlaceholderText
            PlaceholderText = "sk-...",
#else
            // Avalonia 11（net8/CI 2.0.1.1）
            Watermark = "sk-...",
#endif
            VerticalAlignment = VerticalAlignment.Center
        };

        _addButton = new Button
        {
            Content = "添加并验证",
            VerticalAlignment = VerticalAlignment.Center
        };
        _addButton.Click += OnAddClick;

        var addRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        addRow.Children.Add(_apiKeyTextBox);
        addRow.Children.Add(_addButton);

        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        _listHeader = new TextBlock
        {
            Text = "API 列表",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = ThemeHelper.GetTextBrush()
        };

        _keyColumnHeader = new TextBlock
        {
            Text = "密钥",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(12, 8, 12, 8),
            Foreground = ThemeHelper.GetTextBrush()
        };
        _statusColumnHeader = new TextBlock
        {
            Text = "状态",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(12, 8, 12, 8),
            Foreground = ThemeHelper.GetTextBrush()
        };

        _tableHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = ThemeHelper.GetCardBackgroundBrush()
        };
        Grid.SetColumn(_keyColumnHeader, 0);
        Grid.SetColumn(_statusColumnHeader, 1);
        _tableHeader.Children.Add(_keyColumnHeader);
        _tableHeader.Children.Add(_statusColumnHeader);

        _apiListBox = new ListBox
        {
            BorderThickness = new Thickness(0),
            MaxHeight = 280,
            ItemTemplate = new FuncDataTemplate<DeepSeekApi>((model, _) => BuildApiRow(model))
        };

        _emptyHint = new TextBlock
        {
            Text = "暂无 API，请先在上方添加。",
            Foreground = ThemeHelper.GetSubTextBrush(),
            Margin = new Thickness(12, 10, 12, 10),
            IsVisible = true
        };

        var tableContent = new StackPanel();
        tableContent.Children.Add(_tableHeader);
        tableContent.Children.Add(_emptyHint);
        tableContent.Children.Add(_apiListBox);

        _tableBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            BorderBrush = ThemeHelper.GetSeparatorBrush(),
            Child = tableContent
        };

        _deleteButton = new Button { Content = "删除选中的 API" };
        _deleteButton.Click += OnDeleteClick;

        _deleteHint = new TextBlock
        {
            Text = "先在上方列表选中一条 API，再点击删除。",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        var deleteRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        deleteRow.Children.Add(_deleteButton);
        deleteRow.Children.Add(_deleteHint);

        _footerHint = new TextBlock
        {
            Text = "提示：API 密钥经 Windows 数据保护（DPAPI）加密后保存在当前用户注册表（HKCU\\Software\\DeepSeekBalance）中，不以明文存储。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        _logHeader = new TextBlock
        {
            Text = "余额变动日志",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = ThemeHelper.GetTextBrush()
        };

        _logApiComboBox = new ComboBox
        {
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemTemplate = new FuncDataTemplate<DeepSeekApi>((model, _) =>
                new TextBlock { Text = model?.MaskedKey ?? string.Empty })
        };
        _logApiComboBox.SelectionChanged += OnLogApiSelectionChanged;

        _logRefreshButton = new Button
        {
            Content = "刷新",
            VerticalAlignment = VerticalAlignment.Center
        };
        _logRefreshButton.Click += OnLogRefreshClick;

        var logToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        logToolbar.Children.Add(_logApiComboBox);
        logToolbar.Children.Add(_logRefreshButton);

        _logTimeColumnHeader = new TextBlock
        {
            Text = "时间",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(12, 8, 12, 8),
            Foreground = ThemeHelper.GetTextBrush()
        };
        _logTypeColumnHeader = new TextBlock
        {
            Text = "类型",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(12, 8, 12, 8),
            Foreground = ThemeHelper.GetTextBrush()
        };
        _logAmountColumnHeader = new TextBlock
        {
            Text = "金额",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = ThemeHelper.GetTextBrush()
        };
        _logBalanceColumnHeader = new TextBlock
        {
            Text = "余额",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = ThemeHelper.GetTextBrush()
        };

        _logTableHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(LogColumnDefinitions),
            Background = ThemeHelper.GetCardBackgroundBrush()
        };
        Grid.SetColumn(_logTimeColumnHeader, 0);
        Grid.SetColumn(_logTypeColumnHeader, 1);
        Grid.SetColumn(_logAmountColumnHeader, 2);
        Grid.SetColumn(_logBalanceColumnHeader, 3);
        _logTableHeader.Children.Add(_logTimeColumnHeader);
        _logTableHeader.Children.Add(_logTypeColumnHeader);
        _logTableHeader.Children.Add(_logAmountColumnHeader);
        _logTableHeader.Children.Add(_logBalanceColumnHeader);

        _logListBox = new ListBox
        {
            BorderThickness = new Thickness(0),
            MaxHeight = 320,
            ItemTemplate = new FuncDataTemplate<BalanceLogEntry>((model, _) => BuildLogRow(model))
        };

        _logEmptyHint = new TextBlock
        {
            Text = "暂无余额变动记录。",
            Foreground = ThemeHelper.GetSubTextBrush(),
            Margin = new Thickness(12, 10, 12, 10),
            IsVisible = true
        };

        var logTableContent = new StackPanel();
        logTableContent.Children.Add(_logTableHeader);
        logTableContent.Children.Add(_logEmptyHint);
        logTableContent.Children.Add(_logListBox);

        _logTableBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            BorderBrush = ThemeHelper.GetSeparatorBrush(),
            Child = logTableContent
        };

        _logFolderHint = new TextBlock
        {
            Text = $"日志永久保存；此处仅显示最近 {MaxLogDisplayCount} 条，完整内容见日志文件。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        _alertHeader = new TextBlock
        {
            Text = "余额阈值预警",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = ThemeHelper.GetTextBrush()
        };

        _alertDescription = new TextBlock
        {
            Text = "开启后，任一已配置 API 的余额不高于阈值时，会通过 ClassIsland 提醒与 Windows 系统通知提示。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        _alertLabel = new TextBlock
        {
            Text = "预警阈值",
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.GetTextBrush()
        };

        _alertThresholdBox = new NumericUpDown
        {
            Minimum = 0,
            // 需求中的上限为 float 上限，但 Avalonia NumericUpDown 使用 decimal，
            // decimal 最大值（约 7.9e28）已远大于任何实际余额，故以 decimal 上限作为无实际限制的上界。
            Maximum = decimal.MaxValue,
            Increment = 0.01m,
            FormatString = "0.00",
            Value = 5m,
            MinWidth = 150,
            VerticalAlignment = VerticalAlignment.Center
        };
        _alertThresholdBox.ValueChanged += (_, _) => ApplyAlertSettings();

        _alertHint = new TextBlock
        {
            Text = "元（最小 0，精确到 0.01；0 表示余额耗尽时提醒）",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.GetSubTextBrush()
        };

        _alertToggle = new ToggleSwitch
        {
            IsChecked = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _alertToggle.IsCheckedChanged += (_, _) => ApplyAlertSettings();

        var alertRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto")
        };
        Grid.SetColumn(_alertLabel, 0);
        Grid.SetColumn(_alertThresholdBox, 1);
        Grid.SetColumn(_alertHint, 2);
        Grid.SetColumn(_alertToggle, 3);
        alertRow.ColumnSpacing = 8;
        alertRow.Children.Add(_alertLabel);
        alertRow.Children.Add(_alertThresholdBox);
        alertRow.Children.Add(_alertHint);
        alertRow.Children.Add(_alertToggle);

        _notifyMethodLabel = new TextBlock
        {
            Text = "提醒方式",
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.GetTextBrush()
        };

        _windowsNotifyCheckBox = new CheckBox
        {
            Content = "Windows通知",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        _windowsNotifyCheckBox.IsCheckedChanged += (_, _) => ApplyAlertSettings();

        _classIslandNotifyCheckBox = new CheckBox
        {
            Content = "ClassIsland提醒",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        _classIslandNotifyCheckBox.IsCheckedChanged += (_, _) => ApplyAlertSettings();

        var notifyMethodPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        notifyMethodPanel.Children.Add(_windowsNotifyCheckBox);
        notifyMethodPanel.Children.Add(_classIslandNotifyCheckBox);

        var notifyMethodRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        Grid.SetColumn(_notifyMethodLabel, 0);
        Grid.SetColumn(notifyMethodPanel, 1);
        notifyMethodRow.Children.Add(_notifyMethodLabel);
        notifyMethodRow.Children.Add(notifyMethodPanel);

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            MaxWidth = 750,
            Margin = new Thickness(0, 4, 0, 12)
        };
        root.Children.Add(_title);
        root.Children.Add(_description);
        root.Children.Add(_alertHeader);
        root.Children.Add(_alertDescription);
        root.Children.Add(alertRow);
        root.Children.Add(notifyMethodRow);
        root.Children.Add(addRow);
        root.Children.Add(_statusText);
        root.Children.Add(_listHeader);
        root.Children.Add(_tableBorder);
        root.Children.Add(deleteRow);
        root.Children.Add(_footerHint);
        root.Children.Add(_logHeader);
        root.Children.Add(logToolbar);
        root.Children.Add(_logTableBorder);
        root.Children.Add(_logFolderHint);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };

        AttachedToVisualTree += (_, _) => OnAttached();
        DetachedFromVisualTree += (_, _) => OnDetached();
    }

    private static DeepSeekService? Service => Plugin.Instance?.Service;

    private void OnAttached()
    {
        var service = Service;
        if (service is not null)
        {
            _apiListBox.ItemsSource = service.Settings.Apis;
            service.Settings.Apis.CollectionChanged += OnApisChanged;

            _logApiComboBox.ItemsSource = service.Settings.Apis;
            if (_logApiComboBox.SelectedItem is null && service.Settings.Apis.Count > 0)
            {
                _logApiComboBox.SelectedIndex = 0;
            }
        }

        UpdateEmptyState();
        ReloadLogs();
        LoadAlertSettings();
        SubscribeTheme();
    }

    private void OnDetached()
    {
        var service = Service;
        if (service is not null)
        {
            service.Settings.Apis.CollectionChanged -= OnApisChanged;
        }

        UnsubscribeTheme();
    }

    private void OnApisChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();

        var service = Service;
        if (service is not null &&
            _logApiComboBox.SelectedItem is null &&
            service.Settings.Apis.Count > 0)
        {
            _logApiComboBox.SelectedIndex = 0;
        }

        ReloadLogs();
    }

    private void UpdateEmptyState()
    {
        var apis = Service?.Settings.Apis;
        var isEmpty = apis is null || apis.Count == 0;
        _emptyHint.IsVisible = isEmpty;
    }

    private static Control BuildApiRow(DeepSeekApi? model)
    {
        // 不显式设置前景色，使其继承设置窗口的主题文本色，自动适配深浅色主题。
        var key = new TextBlock
        {
            Text = model?.MaskedKey ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };

        var status = new TextBlock
        {
            Text = "已验证",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            Opacity = 0.6
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(key, 0);
        Grid.SetColumn(status, 1);
        grid.Children.Add(key);
        grid.Children.Add(status);
        return grid;
    }

    /// <summary>
    /// 构建一行余额变动日志：时间 | 类型 | 金额 | 余额。
    /// 充值金额显示为绿色、消费为红色。
    /// </summary>
    private static Control BuildLogRow(BalanceLogEntry? model)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(LogColumnDefinitions) };

        var time = new TextBlock
        {
            Text = model?.TimeText ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 6, 12, 6)
        };
        var type = new TextBlock
        {
            Text = model?.Type ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 6, 12, 6)
        };
        var amount = new TextBlock
        {
            Text = model?.DeltaText ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = model is null ? null : (model.Delta >= 0 ? SuccessBrush : ErrorBrush)
        };
        var balance = new TextBlock
        {
            Text = model?.BalanceText ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = 0.8
        };

        Grid.SetColumn(time, 0);
        Grid.SetColumn(type, 1);
        Grid.SetColumn(amount, 2);
        Grid.SetColumn(balance, 3);
        grid.Children.Add(time);
        grid.Children.Add(type);
        grid.Children.Add(amount);
        grid.Children.Add(balance);
        return grid;
    }

    private void OnLogApiSelectionChanged(object? sender, SelectionChangedEventArgs e) => ReloadLogs();

    private void OnLogRefreshClick(object? sender, RoutedEventArgs e) => ReloadLogs();

    /// <summary>
    /// 重新读取并展示所选 API 最近的余额变动日志。
    /// </summary>
    private void ReloadLogs()
    {
        var service = Service;
        if (service is null)
        {
            return;
        }

        _logFolderHint.Text =
            $"日志永久保存；此处仅显示最近 {MaxLogDisplayCount} 条，完整内容见日志文件：{service.Log.FolderPath}";

        if (_logApiComboBox.SelectedItem is not DeepSeekApi api)
        {
            _logListBox.ItemsSource = null;
            _logEmptyHint.Text = service.Settings.Apis.Count == 0
                ? "尚未配置任何 API，添加后即可记录余额变动。"
                : "请选择要查看日志的 API。";
            _logEmptyHint.IsVisible = true;
            return;
        }

        IReadOnlyList<BalanceLogEntry> entries;
        try
        {
            entries = service.Log.ReadRecent(api.MaskedKey, MaxLogDisplayCount);
        }
        catch
        {
            entries = Array.Empty<BalanceLogEntry>();
        }

        _logListBox.ItemsSource = entries;
        _logEmptyHint.Text = "该 API 暂无余额变动记录。";
        _logEmptyHint.IsVisible = entries.Count == 0;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        var service = Service;
        if (service is null)
        {
            return;
        }

        var key = _apiKeyTextBox.Text?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            SetStatus("请输入 API 密钥。", isError: true);
            return;
        }

        if (!key.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("仅支持 DeepSeek API 密钥（应以 sk- 开头）。", isError: true);
            return;
        }

        _addButton.IsEnabled = false;
        SetStatus("正在验证密钥…", isError: false);
        try
        {
            var (ok, message) = await service.TryAddApiAsync(key);
            SetStatus(message, isError: !ok);
            if (ok)
            {
                _apiKeyTextBox.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"添加失败：{ex.Message}", isError: true);
        }
        finally
        {
            _addButton.IsEnabled = true;
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var service = Service;
        if (service is null)
        {
            return;
        }

        if (_apiListBox.SelectedItem is not DeepSeekApi api)
        {
            SetStatus("请先在列表中选择要删除的 API。", isError: true);
            return;
        }

        var maskedKey = api.MaskedKey;
        service.RemoveApi(api);
        _apiListBox.SelectedItem = null;
        SetStatus($"已删除 {maskedKey}。", isError: false);
    }

    /// <summary>
    /// 从插件级预警设置载入控件当前值。
    /// </summary>
    private void LoadAlertSettings()
    {
        var service = Service;
        if (service is null)
        {
            return;
        }

        _loadingAlertSettings = true;
        try
        {
            _alertToggle.IsChecked = service.AlertSettings.IsEnabled;
            _alertThresholdBox.Value = service.AlertSettings.Threshold;
            _windowsNotifyCheckBox.IsChecked = service.AlertSettings.NotifyViaWindows;
            _classIslandNotifyCheckBox.IsChecked = service.AlertSettings.NotifyViaClassIsland;
        }
        finally
        {
            _loadingAlertSettings = false;
        }
    }

    /// <summary>
    /// 将控件当前值写回插件级预警设置并持久化。
    /// </summary>
    private void ApplyAlertSettings()
    {
        if (_loadingAlertSettings)
        {
            return;
        }

        var service = Service;
        if (service is null)
        {
            return;
        }

        service.AlertSettings.IsEnabled = _alertToggle.IsChecked == true;
        service.AlertSettings.Threshold = Math.Round(
            _alertThresholdBox.Value ?? 0m,
            2,
            MidpointRounding.AwayFromZero);
        service.AlertSettings.NotifyViaWindows = _windowsNotifyCheckBox.IsChecked == true;
        service.AlertSettings.NotifyViaClassIsland = _classIslandNotifyCheckBox.IsChecked == true;
        service.SaveAlertSettings();
    }

    private void SetStatus(string message, bool isError)
    {
        _statusText.Text = message;
        _statusText.Foreground = isError ? ErrorBrush : SuccessBrush;
        _statusText.IsVisible = true;
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
        _title.Foreground = ThemeHelper.GetTextBrush();
        _description.Foreground = ThemeHelper.GetSubTextBrush();
        _alertHeader.Foreground = ThemeHelper.GetTextBrush();
        _alertDescription.Foreground = ThemeHelper.GetSubTextBrush();
        _alertLabel.Foreground = ThemeHelper.GetTextBrush();
        _alertHint.Foreground = ThemeHelper.GetSubTextBrush();
        _notifyMethodLabel.Foreground = ThemeHelper.GetTextBrush();
        _listHeader.Foreground = ThemeHelper.GetTextBrush();
        _keyColumnHeader.Foreground = ThemeHelper.GetTextBrush();
        _statusColumnHeader.Foreground = ThemeHelper.GetTextBrush();
        _deleteHint.Foreground = ThemeHelper.GetSubTextBrush();
        _footerHint.Foreground = ThemeHelper.GetSubTextBrush();
        _emptyHint.Foreground = ThemeHelper.GetSubTextBrush();
        _tableHeader.Background = ThemeHelper.GetCardBackgroundBrush();
        _tableBorder.BorderBrush = ThemeHelper.GetSeparatorBrush();
        _logHeader.Foreground = ThemeHelper.GetTextBrush();
        _logTimeColumnHeader.Foreground = ThemeHelper.GetTextBrush();
        _logTypeColumnHeader.Foreground = ThemeHelper.GetTextBrush();
        _logAmountColumnHeader.Foreground = ThemeHelper.GetTextBrush();
        _logBalanceColumnHeader.Foreground = ThemeHelper.GetTextBrush();
        _logEmptyHint.Foreground = ThemeHelper.GetSubTextBrush();
        _logFolderHint.Foreground = ThemeHelper.GetSubTextBrush();
        _logTableHeader.Background = ThemeHelper.GetCardBackgroundBrush();
        _logTableBorder.BorderBrush = ThemeHelper.GetSeparatorBrush();
    }
}

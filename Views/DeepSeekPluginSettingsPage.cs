using System;
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

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            MaxWidth = 750,
            Margin = new Thickness(0, 4, 0, 12)
        };
        root.Children.Add(_title);
        root.Children.Add(_description);
        root.Children.Add(addRow);
        root.Children.Add(_statusText);
        root.Children.Add(_listHeader);
        root.Children.Add(_tableBorder);
        root.Children.Add(deleteRow);
        root.Children.Add(_footerHint);

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
        }

        UpdateEmptyState();
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

    private void OnApisChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

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
        _listHeader.Foreground = ThemeHelper.GetTextBrush();
        _keyColumnHeader.Foreground = ThemeHelper.GetTextBrush();
        _statusColumnHeader.Foreground = ThemeHelper.GetTextBrush();
        _deleteHint.Foreground = ThemeHelper.GetSubTextBrush();
        _footerHint.Foreground = ThemeHelper.GetSubTextBrush();
        _emptyHint.Foreground = ThemeHelper.GetSubTextBrush();
        _tableHeader.Background = ThemeHelper.GetCardBackgroundBrush();
        _tableBorder.BorderBrush = ThemeHelper.GetSeparatorBrush();
    }
}

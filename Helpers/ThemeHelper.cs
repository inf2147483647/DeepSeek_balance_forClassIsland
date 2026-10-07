﻿using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace DeepSeekBalance.Helpers;

/// <summary>
/// 提供随应用深浅色主题变化的画刷，用于让界面文本自适应主题。
/// </summary>
public static class ThemeHelper
{
    public static bool IsDarkTheme()
    {
        var variant = Application.Current?.ActualThemeVariant;
        if (variant == ThemeVariant.Dark)
        {
            return true;
        }

        if (variant == ThemeVariant.Light)
        {
            return false;
        }

        return true;
    }

    public static IBrush GetTextBrush()
        => IsDarkTheme() ? Brushes.White : Brushes.Black;

    public static IBrush GetSubTextBrush()
        => IsDarkTheme() ? new SolidColorBrush(Color.Parse("#C8C8C8")) : new SolidColorBrush(Color.Parse("#555555"));

    public static IBrush GetSeparatorBrush()
        => IsDarkTheme() ? new SolidColorBrush(Color.Parse("#555555")) : new SolidColorBrush(Color.Parse("#CCCCCC"));

    public static IBrush GetCardBackgroundBrush()
        => IsDarkTheme() ? new SolidColorBrush(Color.Parse("#2D2D30")) : new SolidColorBrush(Color.Parse("#F5F5F5"));
}

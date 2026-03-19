using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace InFalsusSongPackStudio.Utils;

// 全局主题管理：支持浅色、深色、跟随系统三种背景模式。
public static class AppThemeManager
{
    public const string ThemeSystem = "system";
    public const string ThemeLight = "light";
    public const string ThemeDark = "dark";

    public static void ApplyTheme(string? themeMode)
    {
        string normalized = NormalizeThemeMode(themeMode);
        bool useDark = normalized == ThemeDark || (normalized == ThemeSystem && IsSystemDarkMode());

        var resources = Application.Current.Resources;
        resources["AppBackgroundBrush"] = BrushFromHex(useDark ? "#0E1423" : "#EEF3FB");
        resources["AppSurfaceBrush"] = BrushFromHex(useDark ? "#13233B" : "#FFFFFF");
        resources["AppSurfaceAltBrush"] = BrushFromHex(useDark ? "#1A2A45" : "#F7FAFF");
        resources["AppAccentBrush"] = BrushFromHex(useDark ? "#2A628A" : "#2D6CB3");
        resources["AppBorderBrush"] = BrushFromHex(useDark ? "#2D4368" : "#C7D7EC");
        resources["AppForegroundBrush"] = BrushFromHex(useDark ? "#F4F8FF" : "#1E304D");
        resources["AppSubtleForegroundBrush"] = BrushFromHex(useDark ? "#AFC6EB" : "#5A7194");
        resources["AppNavItemBrush"] = BrushFromHex(useDark ? "#1F2E4A" : "#E5EEF9");
        resources["AppNavItemHoverBrush"] = BrushFromHex(useDark ? "#2B446E" : "#D7E7FA");
        resources["AppNavItemActiveBrush"] = BrushFromHex(useDark ? "#355587" : "#C6DCF8");
        resources["AppNavItemSelectedBrush"] = BrushFromHex(useDark ? "#1B3150" : "#3D6595");
        resources[SystemColors.WindowBrushKey] = BrushFromHex(useDark ? "#13233B" : "#FFFFFF");
        resources[SystemColors.ControlBrushKey] = BrushFromHex(useDark ? "#13233B" : "#FFFFFF");
        resources[SystemColors.ControlLightBrushKey] = BrushFromHex(useDark ? "#1A2A45" : "#F2F4F8");
        resources[SystemColors.ControlLightLightBrushKey] = BrushFromHex(useDark ? "#203252" : "#FFFFFF");
        resources[SystemColors.ControlDarkBrushKey] = BrushFromHex(useDark ? "#2D4368" : "#BAC8DC");
        resources[SystemColors.ControlDarkDarkBrushKey] = BrushFromHex(useDark ? "#2D4368" : "#A6B7CF");
        resources[SystemColors.WindowTextBrushKey] = BrushFromHex(useDark ? "#F4F8FF" : "#1E304D");
        resources[SystemColors.ControlTextBrushKey] = BrushFromHex(useDark ? "#F4F8FF" : "#1E304D");
        resources[SystemColors.HighlightBrushKey] = BrushFromHex(useDark ? "#355587" : "#C6DCF8");
        resources[SystemColors.InactiveSelectionHighlightBrushKey] = BrushFromHex(useDark ? "#2B446E" : "#D7E7FA");
        resources[SystemColors.HighlightTextBrushKey] = BrushFromHex(useDark ? "#F4F8FF" : "#1E304D");
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = BrushFromHex(useDark ? "#F4F8FF" : "#1E304D");
    }

    public static string NormalizeThemeMode(string? mode)
    {
        string value = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            ThemeLight => ThemeLight,
            ThemeDark => ThemeDark,
            _ => ThemeSystem
        };
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using RegistryKey? personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = personalize?.GetValue("AppsUseLightTheme");
            if (value is int i)
                return i == 0;
        }
        catch
        {
        }

        return false;
    }

    private static Brush BrushFromHex(string hex)
        => (Brush)new BrushConverter().ConvertFromString(hex)!;
}

using System.Windows;
using Microsoft.Win32;
using InFalsusSongPackStudio.Utils;
using System.IO;
using System.Linq;

namespace InFalsusSongPackStudio.Views;

// 设置窗口，管理预览与编辑相关的用户配置项。
public partial class SettingsWindow : Window
{
    public static readonly DependencyProperty IsEmbeddedHostProperty =
        DependencyProperty.Register(nameof(IsEmbeddedHost), typeof(bool), typeof(SettingsWindow), new PropertyMetadata(false));

    public static readonly DependencyProperty GameDirectoryProperty =
        DependencyProperty.Register(nameof(GameDirectory), typeof(string), typeof(SettingsWindow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ThemeModeProperty =
        DependencyProperty.Register(nameof(ThemeMode), typeof(string), typeof(SettingsWindow), new PropertyMetadata(AppThemeManager.ThemeSystem));

    public static readonly DependencyProperty AutoRenameWhenTargetLockedProperty =
        DependencyProperty.Register(nameof(AutoRenameWhenTargetLocked), typeof(bool), typeof(SettingsWindow), new PropertyMetadata(true));

    public bool IsEmbeddedHost
    {
        get => (bool)GetValue(IsEmbeddedHostProperty);
        set => SetValue(IsEmbeddedHostProperty, value);
    }

    public string GameDirectory
    {
        get => (string)GetValue(GameDirectoryProperty);
        set => SetValue(GameDirectoryProperty, value);
    }

    public string ThemeMode
    {
        get => (string)GetValue(ThemeModeProperty);
        set => SetValue(ThemeModeProperty, AppThemeManager.NormalizeThemeMode(value));
    }

    public bool AutoRenameWhenTargetLocked
    {
        get => (bool)GetValue(AutoRenameWhenTargetLockedProperty);
        set => SetValue(AutoRenameWhenTargetLockedProperty, value);
    }

    public event EventHandler? SettingsApplied;
    public event EventHandler? CloseRequested;

    // 初始化设置窗口。
    public SettingsWindow()
    {
        InitializeComponent();
        ReloadGlobalSettings();
    }

    // 保存设置窗口中的修改并关闭窗口。
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ApplySettings();

        if (IsEmbeddedHost)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (IsEmbeddedHost)
        {
            ReloadGlobalSettings();
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        DialogResult = false;
        Close();
    }

    private void ApplySettings()
    {
        SyncGlobalSettingsFromUi();

        var settings = AppGlobalSettingsStore.Load();
        settings.GameDirectory = GameDirectory?.Trim() ?? string.Empty;
        settings.ThemeMode = AppThemeManager.NormalizeThemeMode(ThemeMode);
        settings.AutoRenameWhenTargetLocked = AutoRenameWhenTargetLocked;
        AppGlobalSettingsStore.Save(settings);
        AppThemeManager.ApplyTheme(settings.ThemeMode);

        SettingsApplied?.Invoke(this, EventArgs.Empty);
    }

    private void BrowseGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "请选择游戏根目录（例如 In Falsus Demo）"
        };

        if (dialog.ShowDialog() != true) return;

        string selected = dialog.FolderName;
        if (!IsValidGameDirectory(selected))
        {
            MessageBox.Show("所选目录无效：未找到 if-app_Data。\n请选择游戏根目录（例如 In Falsus Demo）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        GameDirectory = selected;
        SyncGlobalUiFromSettings();

        // 导入目录后立即应用，无需额外点击“应用/确定”。
        ApplySettings();
        MessageBox.Show("游戏目录已保存并自动应用。", "设置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReloadGlobalSettings()
    {
        var settings = AppGlobalSettingsStore.Load();
        GameDirectory = settings.GameDirectory ?? string.Empty;
        ThemeMode = AppThemeManager.NormalizeThemeMode(settings.ThemeMode);
        AutoRenameWhenTargetLocked = settings.AutoRenameWhenTargetLocked;
        SyncGlobalUiFromSettings();
    }

    private void SyncGlobalUiFromSettings()
    {
        TbGameDirectory.Text = GameDirectory;
        CbThemeMode.SelectedValue = AppThemeManager.NormalizeThemeMode(ThemeMode);
        CbAutoRenameWhenLocked.IsChecked = AutoRenameWhenTargetLocked;
    }

    private void SyncGlobalSettingsFromUi()
    {
        GameDirectory = TbGameDirectory.Text?.Trim() ?? string.Empty;

        string selectedTheme = CbThemeMode.SelectedValue as string ?? AppThemeManager.ThemeSystem;
        ThemeMode = AppThemeManager.NormalizeThemeMode(selectedTheme);
        AutoRenameWhenTargetLocked = CbAutoRenameWhenLocked.IsChecked == true;
    }

    private static bool IsValidGameDirectory(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return false;

        string root = Path.GetFullPath(gameDirectory);
        return Directory.Exists(Path.Combine(root, "if-app_Data"));
    }

    // 打开自定义映射配置窗口。
    private void OpenCustomMapping_Click(object sender, RoutedEventArgs e)
    {
        var win = new CustomMappingWindow { DataContext = this.DataContext };

        // 嵌入模式下当前 SettingsWindow 未显示，不能直接作为 Owner。
        Window? owner = (!IsEmbeddedHost && IsVisible)
            ? this
            : Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

        if (owner != null)
            win.Owner = owner;

        win.ShowDialog();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using InFalsusSongPackStudio.Utils;

namespace InFalsusSongPackStudio.Views;

// 新版主壳窗口：所有功能在主窗口内容区切换，不再从侧边栏弹新窗。
public partial class ShellWindow : Window
{
    private static readonly System.Windows.Media.FontFamily NavIconFontFamily = new("Segoe Fluent Icons");
    private static readonly Duration SidebarAnimationDuration = new(TimeSpan.FromMilliseconds(180));

    public double SidebarAnimatedWidth
    {
        get => (double)GetValue(SidebarAnimatedWidthProperty);
        set => SetValue(SidebarAnimatedWidthProperty, value);
    }

    public static readonly DependencyProperty SidebarAnimatedWidthProperty =
        DependencyProperty.Register(nameof(SidebarAnimatedWidth), typeof(double), typeof(ShellWindow),
            new PropertyMetadata(280.0, OnSidebarAnimatedWidthChanged));

    private static void OnSidebarAnimatedWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ShellWindow window)
            window.SidebarColumn.Width = new GridLength((double)e.NewValue);
    }

    private enum ShellSection
    {
        Home,
        Bundle,
        Batch,
        Restore,
        AffConverter,
        Preview,
        Settings
    }

    private sealed class EmbeddedSectionHost
    {
        public Window? WindowHost { get; init; }
        public required UIElement Content { get; init; }
    }

    private readonly Dictionary<ShellSection, EmbeddedSectionHost> _embeddedHosts = new();
    private readonly Dictionary<ShellSection, Button> _navButtons;
    private readonly Dictionary<Button, string> _navButtonGlyphs = new();
    private bool _isSidebarCollapsed;
    private bool _isConverterSubmenuExpanded;
    private string _pendingRestoreGameDirectory = string.Empty;

    public ShellWindow()
    {
        InitializeComponent();
        SidebarAnimatedWidth = SidebarColumn.Width.Value;

        _navButtons = new Dictionary<ShellSection, Button>
        {
            [ShellSection.Home] = BtnNavHome,
            [ShellSection.Bundle] = BtnNavBundle,
            [ShellSection.Batch] = BtnNavBatch,
            [ShellSection.Restore] = BtnNavRestore,
            [ShellSection.AffConverter] = BtnNavAffConverter,
            [ShellSection.Preview] = BtnNavPreview,
            [ShellSection.Settings] = BtnNavSettings
        };

        _navButtonGlyphs[BtnNavHome] = "\uE80F";
        _navButtonGlyphs[BtnNavBundle] = "\uE7B8";
        _navButtonGlyphs[BtnNavBatch] = "\uE8FD";
        _navButtonGlyphs[BtnNavRestore] = "\uE81C";
        _navButtonGlyphs[BtnNavConverterGroup] = "\uE8AB";
        _navButtonGlyphs[BtnNavAffConverter] = "\uE8AB";
        _navButtonGlyphs[BtnNavPreview] = "\uE890";
        _navButtonGlyphs[BtnNavSettings] = "\uE713";
        _navButtonGlyphs[BtnNavAbout] = "\uE946";

        BtnToggleSidebar.FontSize = 18;
        BtnToggleSidebar.FontWeight = FontWeights.Bold;
        UpdateToggleGlyph();
        ApplySidebarToggleLayout();
        ApplyConverterSubmenuState();

        ApplyNavButtonContents();

        RefreshGameDirectoryText();
        SwitchSection(ShellSection.Home);
    }

    private void NavHome_Click(object sender, RoutedEventArgs e)
        => SwitchSection(ShellSection.Home);

    private void NavBundle_Click(object sender, RoutedEventArgs e)
        => SwitchSection(ShellSection.Bundle);

    private void NavBatch_Click(object sender, RoutedEventArgs e)
        => SwitchSection(ShellSection.Batch);

    private void NavAffConverter_Click(object sender, RoutedEventArgs e)
        => SwitchSection(ShellSection.AffConverter);

    private void NavPreview_Click(object sender, RoutedEventArgs e)
        => SwitchSection(ShellSection.Preview);

    public void NavigateToPreviewSection()
        => SwitchSection(ShellSection.Preview);

    private void NavSettings_Click(object sender, RoutedEventArgs e)
        => SwitchSection(ShellSection.Settings);

    private void SwitchSection(ShellSection section)
    {
        RefreshGameDirectoryText();
        UpdateNavHighlight(section);
        RestorePromptCard.Visibility = Visibility.Collapsed;

        if (section == ShellSection.Home)
        {
            HomePanel.Visibility = Visibility.Visible;
            SectionContent.Visibility = Visibility.Collapsed;
            SectionContent.Content = null;
            FadeInElement(HomePanel);
            return;
        }

        if (section == ShellSection.Restore)
        {
            HomePanel.Visibility = Visibility.Collapsed;
            SectionContent.Visibility = Visibility.Collapsed;
            SectionContent.Content = null;
            RestorePromptCard.Visibility = Visibility.Visible;
            FadeInElement(RestorePromptCard);
            return;
        }

        HomePanel.Visibility = Visibility.Collapsed;
        SectionContent.Visibility = Visibility.Visible;

        if (section == ShellSection.Preview)
        {
            var previewConverter = GetOrCreateConverterPage();
            if (previewConverter.HasLoadedSpcForPreview)
            {
                SectionContent.Content = previewConverter;
                previewConverter.EnterPreviewNavigationMode();
                FadeInElement(SectionContent);
                return;
            }

            var previewHost = GetOrCreateEmbeddedHost(ShellSection.Preview);
            if (previewHost.Content is PreviewImportPage previewPage)
                previewPage.SetStatus("尚未导入 SPC。", false);

            SectionContent.Content = previewHost.Content;
            FadeInElement(SectionContent);
            return;
        }

        var host = GetOrCreateEmbeddedHost(section);
        SectionContent.Content = host.Content;
        FadeInElement(SectionContent);

        if (host.Content is ConverterPage converterPage)
        {
            if (section == ShellSection.Preview)
            {
                converterPage.EnterPreviewNavigationMode();
            }
            else if (section == ShellSection.AffConverter)
            {
                converterPage.EnterConversionNavigationMode();
            }
        }

    }

    private EmbeddedSectionHost GetOrCreateEmbeddedHost(ShellSection section)
    {
        if (_embeddedHosts.TryGetValue(section, out var existing))
            return existing;

        EmbeddedSectionHost created = section switch
        {
            ShellSection.Bundle => CreateEmbeddedFromWindow(new BundleTexturePackageWindow { Owner = this }),
            ShellSection.Batch => CreateEmbeddedBatchHost(),
            ShellSection.AffConverter => GetOrCreateConverterHost(),
            ShellSection.Preview => CreatePreviewImportHost(),
            ShellSection.Settings => CreateEmbeddedSettingsHost(),
            _ => throw new InvalidOperationException("Unsupported section.")
        };

        _embeddedHosts[section] = created;
        return created;
    }

    private EmbeddedSectionHost GetOrCreateConverterHost()
    {
        if (_embeddedHosts.TryGetValue(ShellSection.AffConverter, out var existing))
            return existing;

        var converterPage = new ConverterPage
        {
            Margin = new Thickness(0)
        };
        var created = new EmbeddedSectionHost
        {
            WindowHost = null,
            Content = converterPage
        };
        _embeddedHosts[ShellSection.AffConverter] = created;
        return created;
    }

    private EmbeddedSectionHost CreatePreviewImportHost()
    {
        var page = new PreviewImportPage();
        page.ImportSpcRequested += ImportPreviewSpc;

        return new EmbeddedSectionHost
        {
            WindowHost = null,
            Content = page
        };
    }

    private EmbeddedSectionHost CreateEmbeddedBatchHost()
    {
        var window = new BatchBundleWindow { Owner = this };
        window.BatchExportCompleted += (_, _) =>
        {
            if (_embeddedHosts.TryGetValue(ShellSection.Bundle, out var bundleHost) && bundleHost.WindowHost is BundleTexturePackageWindow bundleWindow)
            {
                bundleWindow.RefreshFromGlobalSettings();
            }
        };

        return CreateEmbeddedFromWindow(window);
    }

    private ConverterPage GetOrCreateConverterPage()
    {
        var host = GetOrCreateConverterHost();
        if (host.Content is ConverterPage page)
            return page;

        throw new InvalidOperationException("Converter host content must be ConverterPage.");
    }

    private void ImportPreviewSpc()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "In Falsus 谱面 (*.spc;*.txt)|*.spc;*.txt|所有文件 (*.*)|*.*",
            Title = "导入预览 SPC"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        var converterPage = GetOrCreateConverterPage();
        if (!converterPage.TryLoadSpcFile(dlg.FileName, out var errorMessage))
        {
            if (_embeddedHosts.TryGetValue(ShellSection.Preview, out var previewHost) && previewHost.Content is PreviewImportPage previewPage)
                previewPage.SetStatus("导入失败，请检查文件格式。", true);

            MessageBox.Show(this, errorMessage ?? "导入失败。", "加载 SPC 失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SectionContent.Content = converterPage;
        converterPage.EnterPreviewNavigationMode();
    }

    private EmbeddedSectionHost CreateEmbeddedSettingsHost()
    {
        var window = new SettingsWindow
        {
            Owner = this,
            IsEmbeddedHost = true
        };

        window.SettingsApplied += (_, _) =>
        {
            RefreshGameDirectoryText();
            AppThemeManager.ApplyTheme(AppGlobalSettingsStore.Load().ThemeMode);

            if (_embeddedHosts.TryGetValue(ShellSection.Bundle, out var bundleHost) && bundleHost.WindowHost is BundleTexturePackageWindow bundleWindow)
            {
                bundleWindow.RefreshFromGlobalSettings();
            }

            if (_embeddedHosts.TryGetValue(ShellSection.Batch, out var batchHost) && batchHost.WindowHost is BatchBundleWindow batchWindow)
            {
                batchWindow.RefreshFromGlobalSettings();
            }

            UpdateNavHighlight(ShellSection.Settings);
        };

        window.CloseRequested += (_, _) => SwitchSection(ShellSection.Home);

        return CreateEmbeddedFromWindow(window);
    }

    private void ToggleConverterSubmenu_Click(object sender, RoutedEventArgs e)
    {
        _isConverterSubmenuExpanded = !_isConverterSubmenuExpanded;
        ApplyConverterSubmenuState();
    }

    private static EmbeddedSectionHost CreateEmbeddedFromWindow(Window window)
    {
        if (window.Content is not UIElement content)
            throw new InvalidOperationException("Window content must be a UIElement.");

        object? hostDataContext = window.DataContext;
        window.Content = null;

        if (content is FrameworkElement fe)
        {
            // 被嵌入后内容树不再继承原 Window.DataContext，这里把上下文转移给内容根元素。
            if (hostDataContext != null && fe.ReadLocalValue(FrameworkElement.DataContextProperty) == DependencyProperty.UnsetValue)
                fe.DataContext = hostDataContext;

            fe.HorizontalAlignment = HorizontalAlignment.Stretch;
            fe.VerticalAlignment = VerticalAlignment.Stretch;
            fe.Margin = new Thickness(0);
        }

        return new EmbeddedSectionHost
        {
            WindowHost = window,
            Content = content
        };
    }

    private void UpdateNavHighlight(ShellSection section)
    {
        foreach (var pair in _navButtons)
        {
            pair.Value.Tag = pair.Key == section ? "__selected__" : ((pair.Value.Tag as string) ?? string.Empty);
        }

        // 非 section 字典里的额外项需恢复非选中态。
        if (BtnNavAbout.Tag as string == "__selected__") BtnNavAbout.Tag = "关于";

        // 重新注入内容，确保 Tag 改变后文字不丢失。
        ApplyNavButtonContents(section);
    }

    private void NavRestore_Click(object sender, RoutedEventArgs e)
    {
        SwitchSection(ShellSection.Restore);

        string gameDirectory = AppGlobalSettingsStore.LoadGameDirectory();
        _pendingRestoreGameDirectory = IsValidGameDirectory(gameDirectory) ? gameDirectory : string.Empty;

        RestoreResultText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(_pendingRestoreGameDirectory))
        {
            RestorePromptText.Text = "未检测到有效游戏目录。请先在设置中配置正确的游戏根目录后再执行恢复。";
            BtnRestoreConfirm.IsEnabled = false;
            return;
        }

        RestorePromptText.Text =
            "将恢复该目录下由“打包谱面”写入的文件：\n" +
            "- 回滚 *_original 备份（sharedassets0.assets/resources.assets/bundle）\n" +
            "- 清理 sam 文件夹中新增加密资源（依据 SongData 备份目录推断）\n\n" +
            "是否继续？";
        BtnRestoreConfirm.IsEnabled = true;
    }

    private async void RestoreConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingRestoreGameDirectory) || !IsValidGameDirectory(_pendingRestoreGameDirectory))
        {
            RestoreResultText.Text = "恢复失败：当前游戏目录无效，请在设置中重新配置。";
            RestoreResultText.Visibility = Visibility.Visible;
            BtnRestoreConfirm.IsEnabled = true;
            return;
        }

        BtnRestoreConfirm.IsEnabled = false;
        BtnRestoreCancel.IsEnabled = false;
        RestoreResultText.Text = "正在恢复，请稍候...";
        RestoreResultText.Visibility = Visibility.Visible;

        try
        {
            string summary = await Task.Run(() => UnitySongResourcePacker.RestoreDeployedSongFiles(_pendingRestoreGameDirectory));
            RestoreResultText.Text = summary;
            RestoreResultText.Visibility = Visibility.Visible;

            if (_embeddedHosts.TryGetValue(ShellSection.Bundle, out var bundleHost) && bundleHost.WindowHost is BundleTexturePackageWindow bundleWindow)
            {
                bundleWindow.RefreshFromGlobalSettings();
            }

            if (_embeddedHosts.TryGetValue(ShellSection.Batch, out var batchHost) && batchHost.WindowHost is BatchBundleWindow batchWindow)
            {
                batchWindow.RefreshFromGlobalSettings();
            }
        }
        catch (Exception ex)
        {
            RestoreResultText.Text = $"恢复失败：{ex.Message}";
            RestoreResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnRestoreConfirm.IsEnabled = true;
            BtnRestoreCancel.IsEnabled = true;
        }
    }

    private void RestoreCancel_Click(object sender, RoutedEventArgs e)
    {
        RestorePromptCard.Visibility = Visibility.Collapsed;
        RestoreResultText.Visibility = Visibility.Collapsed;
        _pendingRestoreGameDirectory = string.Empty;
    }

    private static bool IsValidGameDirectory(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return false;

        string root = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(root))
            return false;

        return Directory.Exists(Path.Combine(root, "if-app_Data"));
    }

    private void RefreshGameDirectoryText()
    {
        string gameDirectory = AppGlobalSettingsStore.LoadGameDirectory();
        if (!IsValidGameDirectory(gameDirectory))
        {
            HomeHintText.Text = "当前未配置有效游戏目录，部分打包功能可能不可用。";
            return;
        }

        HomeHintText.Text = "游戏目录已配置，可直接进入打包或预览流程。";
    }

    private void ShowAbout_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
        => Close();

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeWindow_Click(sender, new RoutedEventArgs());
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;

        AnimateSidebarWidth(_isSidebarCollapsed ? 88 : 280);
        SidebarRoot.Padding = _isSidebarCollapsed ? new Thickness(10) : new Thickness(16);
        BrandSubTitleText.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        BrandTitleText.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        UpdateToggleGlyph();
        ApplySidebarToggleLayout();
        ApplyConverterSubmenuState();

        ApplyNavButtonContents();
    }

    private void AnimateSidebarWidth(double targetWidth)
    {
        double from = SidebarAnimatedWidth;
        var animation = new DoubleAnimation
        {
            From = from,
            To = targetWidth,
            Duration = SidebarAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(SidebarAnimatedWidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyConverterSubmenuState()
    {
        if (_isSidebarCollapsed)
        {
            ConverterSubmenuPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ConverterSubmenuPanel.Visibility = _isConverterSubmenuExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateToggleGlyph()
    {
        BtnToggleSidebar.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        BtnToggleSidebar.Content = _isSidebarCollapsed ? ">" : "☰";
    }

    private void ApplySidebarToggleLayout()
    {
        if (_isSidebarCollapsed)
        {
            Grid.SetColumn(BtnToggleSidebar, 0);
            Grid.SetColumnSpan(BtnToggleSidebar, 2);
            BtnToggleSidebar.HorizontalAlignment = HorizontalAlignment.Center;
            BtnToggleSidebar.Margin = new Thickness(0);
            return;
        }

        Grid.SetColumn(BtnToggleSidebar, 1);
        Grid.SetColumnSpan(BtnToggleSidebar, 1);
        BtnToggleSidebar.HorizontalAlignment = HorizontalAlignment.Right;
        BtnToggleSidebar.Margin = new Thickness(8, 0, 0, 0);
    }

    private void ApplyNavButtonContents(ShellSection? selected = null)
    {
        Button? selectedButton = null;
        if (selected.HasValue && _navButtons.TryGetValue(selected.Value, out var selectedFromArg))
        {
            selectedButton = selectedFromArg;
        }
        else
        {
            selectedButton = _navButtons.Values.FirstOrDefault(b => Equals(b.Tag, "__selected__"));
        }

        foreach (var button in _navButtons.Values.Concat(new[] { BtnNavConverterGroup, BtnNavAbout }))
        {
            string label = button switch
            {
                _ when button == BtnNavHome => "主页",
                _ when button == BtnNavBundle => "打包谱面",
                _ when button == BtnNavBatch => "批量打包",
                _ when button == BtnNavRestore => "恢复写入文件",
                _ when button == BtnNavConverterGroup => "谱面转换",
                _ when button == BtnNavAffConverter => "AFF → SPC转换",
                _ when button == BtnNavPreview => "谱面预览",
                _ when button == BtnNavSettings => "设置",
                _ when button == BtnNavAbout => "关于",
                _ => ""
            };

            string glyph = _navButtonGlyphs.TryGetValue(button, out var g) ? g : "\uE10F";

            button.HorizontalContentAlignment = _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.Padding = _isSidebarCollapsed ? new Thickness(0) : new Thickness(10, 0, 10, 0);
            button.ToolTip = label;

            if (_isSidebarCollapsed)
            {
                var iconOnlyText = new TextBlock
                {
                    Text = glyph,
                    FontFamily = NavIconFontFamily,
                    FontSize = 19,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Width = 20
                };
                iconOnlyText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = button });
                button.Content = iconOnlyText;
            }
            else
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                var iconText = new TextBlock
                {
                    Text = glyph,
                    FontFamily = NavIconFontFamily,
                    FontSize = 15.5,
                    FontWeight = FontWeights.SemiBold,
                    Width = 19,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                iconText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = button });
                panel.Children.Add(iconText);

                var labelText = new TextBlock
                {
                    Text = label,
                    Margin = new Thickness(8, 0, 0, 0),
                    FontSize = button == BtnNavAffConverter ? 12 : (button == BtnNavSettings || button == BtnNavAbout ? 12.5 : 13),
                    FontWeight = button == BtnNavAffConverter || button == BtnNavSettings || button == BtnNavAbout ? FontWeights.Normal : FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                };
                labelText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = button });
                panel.Children.Add(labelText);
                button.Content = panel;
            }

            button.Tag = button == selectedButton ? "__selected__" : label;
        }
    }

    private static void FadeInElement(UIElement element)
    {
        element.Opacity = 0;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}

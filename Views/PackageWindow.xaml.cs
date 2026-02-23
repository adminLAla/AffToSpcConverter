using System;
using System.Windows;
using Microsoft.Win32;
using System.IO;
using AffToSpcConverter.Utils;
using AffToSpcConverter.ViewModels;

namespace AffToSpcConverter.Views;

public partial class PackageWindow : Window
{
    private readonly PackageViewModel _vm = new PackageViewModel();
    private const string MappingFileName = "StreamingAssetsMapping.json";

    // 初始化打包窗口并绑定视图模型。
    public PackageWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    // 选择待打包的源资源文件。
    private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog() == true)
        {
            _vm.SourceFilePath = dialog.FileName;
        }
    }

    // 选择打包输出目录。
    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        // WPF 中文件夹选择对话框的可用 API 会随目标框架变化。
        // 这里使用 .NET 8 的 OpenFolderDialog（Windows）。
        // 若后续降级框架，可改回 WinForms 或第三方文件夹选择器。
        
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
             _vm.OutputDirectory = dialog.FolderName;
        }
    }

    // 执行资源加密打包并更新状态提示。
    private void BtnPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.Status = "处理中...";
            var mappingPath = Path.Combine(AppContext.BaseDirectory, MappingFileName);
            if (!File.Exists(mappingPath))
                throw new FileNotFoundException($"未找到映射文件：{mappingPath}");

            GameAssetPacker.Pack(_vm.SourceFilePath, _vm.OriginalFilename, mappingPath, _vm.OutputDirectory);
            _vm.Status = "打包成功。";
            MessageBox.Show("资源已加密并打包完成。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _vm.Status = $"打包失败：{ex.Message}";
            MessageBox.Show($"资源打包失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

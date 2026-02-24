using System;
using System.Windows;
using Microsoft.Win32;
using System.IO;
using AffToSpcConverter.Utils;
using AffToSpcConverter.ViewModels;
using System.ComponentModel;
using System.Linq;

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
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        RefreshTargetCandidates();
    }

    // 在源文件变化后刷新可替换目标下拉列表。
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageViewModel.SourceFilePath))
            RefreshTargetCandidates();
    }

    // 按映射表与源文件类型刷新“替换目标”下拉列表。
    private void RefreshTargetCandidates()
    {
        try
        {
            var mappingPath = Path.Combine(AppContext.BaseDirectory, MappingFileName);
            string? previousSelection = string.IsNullOrWhiteSpace(_vm.SelectedTargetLookupPath)
                ? null
                : _vm.SelectedTargetLookupPath;

            _vm.TargetLookupCandidates.Clear();

            if (!File.Exists(mappingPath))
            {
                _vm.Status = $"未找到映射文件：{mappingPath}";
                _vm.SelectedTargetLookupPath = "";
                return;
            }

            var candidates = GameAssetPacker.GetReplacementCandidates(mappingPath, _vm.SourceFilePath);
            foreach (var path in candidates)
                _vm.TargetLookupCandidates.Add(path);

            if (!string.IsNullOrWhiteSpace(previousSelection)
                && _vm.TargetLookupCandidates.Contains(previousSelection))
            {
                _vm.SelectedTargetLookupPath = previousSelection;
            }
            else
            {
                _vm.SelectedTargetLookupPath = _vm.TargetLookupCandidates.FirstOrDefault() ?? "";
            }

            string sourceExt = Path.GetExtension(_vm.SourceFilePath ?? "").ToLowerInvariant();
            if (sourceExt is ".ogg")
                _vm.Status = $"已加载 {_vm.TargetLookupCandidates.Count} 个可替换目标（音频：仅显示 .wav/.ogg）。";
            else if (sourceExt is ".txt" or ".spc")
                _vm.Status = $"已加载 {_vm.TargetLookupCandidates.Count} 个可替换目标（谱面：仅显示 .spc）。";
            else
                _vm.Status = $"已加载 {_vm.TargetLookupCandidates.Count} 个可替换目标。";
        }
        catch (Exception ex)
        {
            _vm.TargetLookupCandidates.Clear();
            _vm.SelectedTargetLookupPath = "";
            _vm.Status = $"读取映射失败：{ex.Message}";
        }
    }

    // 选择待打包的谱面或音乐文件（仅支持 .txt/.spc/.ogg）。
    private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择谱面/音乐文件",
            Filter = "谱面/音乐文件 (*.txt;*.spc;*.ogg)|*.txt;*.spc;*.ogg|谱面文件 (*.txt;*.spc)|*.txt;*.spc|音乐文件 (*.ogg)|*.ogg",
            CheckFileExists = true
        };
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
            if (string.IsNullOrWhiteSpace(_vm.SelectedTargetLookupPath))
                throw new InvalidOperationException("请选择要替换的目标资源路径。");

            GameAssetPacker.Pack(_vm.SourceFilePath, _vm.SelectedTargetLookupPath, mappingPath, _vm.OutputDirectory);
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

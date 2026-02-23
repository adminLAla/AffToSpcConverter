using System;
using System.IO;
using System.Linq;
using System.Windows;
using AffToSpcConverter.Utils;
using AffToSpcConverter.ViewModels;
using Microsoft.Win32;

namespace AffToSpcConverter.Views;

public partial class BundleTexturePackageWindow : Window
{
    private readonly BundleTexturePackageViewModel _vm = new();

    // 初始化未加密 bundle 纹理替换窗口。
    public BundleTexturePackageWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    // 选择导入的曲绘图片（支持 PNG/JPG/JPEG）。
    private void BtnBrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入曲绘",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg"
        };

        if (dialog.ShowDialog() != true) return;

        _vm.ImageFilePath = dialog.FileName;
        string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        _vm.Status = ext is ".jpg" or ".jpeg"
            ? "已导入 JPG/JPEG 图片，导出时会先自动转换为 PNG 再写入 Texture2D。"
            : "已导入 PNG 图片。";
    }

    // 选择未加密 Unity bundle，并读取其中的 Texture2D 列表。
    private void BtnBrowseBundle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入未加密 .bundle",
            Filter = "Unity AssetBundle (*.bundle)|*.bundle|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        _vm.BundleFilePath = dialog.FileName;
        if (string.IsNullOrWhiteSpace(_vm.OutputDirectory))
            _vm.OutputDirectory = Path.GetDirectoryName(dialog.FileName) ?? "";

        ReloadTextureCandidates();
    }

    // 选择导出 bundle 的目标文件夹。
    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() != true) return;

        _vm.OutputDirectory = dialog.FolderName;
    }

    // 执行纹理替换并导出新的 bundle 文件。
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_vm.ImageFilePath))
                throw new Exception("请先导入曲绘图片。");
            if (string.IsNullOrWhiteSpace(_vm.BundleFilePath))
                throw new Exception("请先导入 .bundle 文件。");
            if (_vm.SelectedTexture == null)
                throw new Exception("请选择要替换的 Texture2D。");
            if (string.IsNullOrWhiteSpace(_vm.OutputDirectory))
                throw new Exception("请选择导出文件夹。");

            _vm.Status = "正在替换纹理并重打包 bundle，请稍候...";

            var result = UnityBundleTexturePacker.ExportBundleWithReplacedTexture(
                _vm.ImageFilePath,
                _vm.BundleFilePath,
                _vm.SelectedTexture,
                _vm.OutputDirectory,
                _vm.AutoRenameWhenTargetLocked,
                _vm.VerifyReadbackSha256);

            _vm.Status =
                $"导出成功：{result.OutputBundlePath}\n" +
                $"写入纹理格式：{result.FinalTextureFormatName}" +
                (result.FallbackToRgba32 ? "（原格式编码失败，已回退 RGBA32）" : "") +
                (result.InputImageConvertedToPng ? "\nJPG/JPEG 已自动转换为 PNG 后写入。" : "") +
                (result.ImageResizedToMatchTexture
                    ? $"\n图片尺寸已自动缩放：{result.InputImageWidth}x{result.InputImageHeight} -> {result.OutputImageWidth}x{result.OutputImageHeight}"
                    : "") +
                (result.AutoRenamedDueToFileLock
                    ? $"\n目标文件被占用，已自动另存为：{Path.GetFileName(result.OutputBundlePath)}"
                    : "") +
                (result.UsedTempFileForInPlaceOverwrite
                    ? "\n导出路径与源 bundle 相同：已使用临时文件写入后覆盖原文件（避免自占用）。"
                    : "") +
                $"\n{result.ReadbackSummary}";

            MessageBox.Show(
                $"导出成功：\n{result.OutputBundlePath}",
                "成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _vm.Status = $"导出失败：{ex.Message}";
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 重新读取 bundle 内 Texture2D 列表并刷新下拉框。
    private void ReloadTextureCandidates()
    {
        try
        {
            _vm.Status = "正在读取 bundle 内纹理列表...";
            _vm.TextureCandidates.Clear();
            _vm.SelectedTexture = null;

            var textures = UnityBundleTexturePacker.ListTextures(_vm.BundleFilePath);
            foreach (var item in textures)
                _vm.TextureCandidates.Add(item);

            _vm.SelectedTexture = _vm.TextureCandidates.FirstOrDefault();

            if (_vm.TextureCandidates.Count == 0)
            {
                _vm.Status = "未在 bundle 中找到可替换的 Texture2D。请确认该 bundle 包含纹理资源，且类型树可读取。";
                return;
            }

            _vm.Status =
                $"已读取 {_vm.TextureCandidates.Count} 个 Texture2D。\n" +
                "请选择一个纹理后导入曲绘并导出新的 .bundle 文件。";
        }
        catch (Exception ex)
        {
            _vm.TextureCandidates.Clear();
            _vm.SelectedTexture = null;
            _vm.Status = $"读取 bundle 失败：{ex.Message}";
            MessageBox.Show($"读取 bundle 失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

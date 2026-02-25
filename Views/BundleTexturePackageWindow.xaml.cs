using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AffToSpcConverter.Utils;
using AffToSpcConverter.ViewModels;
using Microsoft.Win32;

namespace AffToSpcConverter.Views;

public partial class BundleTexturePackageWindow : Window
{
    private readonly BundleTexturePackageViewModel _vm = new();
    private SongBundleScanResult? _bundleScan;

    // 初始化“打包谱面”窗口。
    public BundleTexturePackageWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // 预置一条谱面项，减少首次使用时的操作步骤。
        if (_vm.ChartRows.Count == 0)
        {
            var row = CreateDefaultChartRow();
            _vm.ChartRows.Add(row);
            _vm.SelectedChartRow = row;
        }
    }

    // 选择游戏根目录（In Falsus Demo），并自动定位 .bundle / sharedassets0.assets。
    private void BtnBrowseGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() != true) return;

        ApplyGameDirectory(dialog.FolderName);
    }

    // 选择新增歌曲曲绘（用于写入新 Texture2D）。
    private void BtnBrowseJacket_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入曲绘",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog() != true) return;

        _vm.JacketImageFilePath = dialog.FileName;
        _vm.Status = "已导入曲绘图片。导出时会自动写入新增 Texture2D。";
    }

    // 选择新增歌曲 BGM（.ogg/.wav），映射扩展名会跟随源文件。
    private void BtnBrowseBgm_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 BGM",
            Filter = "音频文件 (*.ogg;*.wav)|*.ogg;*.wav|Ogg 音频 (*.ogg)|*.ogg|Wav 音频 (*.wav)|*.wav"
        };
        if (dialog.ShowDialog() != true) return;

        _vm.BgmFilePath = dialog.FileName;
        _vm.Status = $"已导入 BGM：{Path.GetFileName(dialog.FileName)}（映射扩展名将跟随源文件）。";
    }

    // 旧版手动选择导出目录入口（当前流程固定输出到游戏根目录\SongData，保留方法以兼容旧 XAML 事件名）。
    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("当前版本会自动输出到游戏根目录下的 SongData 文件夹，无需手动选择。", "提示",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // 重新扫描 .bundle，读取 SongDatabase 空槽和可作为模板的曲绘资源。
    private void BtnRescanBundle_Click(object sender, RoutedEventArgs e)
    {
        ReloadBundleScan();
    }

    // 添加一条 ChartInfo 配置行（对应一个谱面分档）。
    private void BtnAddChartRow_Click(object sender, RoutedEventArgs e)
    {
        var row = CreateDefaultChartRow();
        _vm.ChartRows.Add(row);
        _vm.SelectedChartRow = row;
        ChartRowsGrid.SelectedItem = row;
        ChartRowsGrid.ScrollIntoView(row);
    }

    // 删除当前选中的 ChartInfo 配置行。
    private void BtnRemoveChartRow_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedChartRow == null)
        {
            MessageBox.Show("请先在表格中选中一条谱面项。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _vm.ChartRows.Remove(_vm.SelectedChartRow);
        _vm.SelectedChartRow = _vm.ChartRows.LastOrDefault();
        ChartRowsGrid.SelectedItem = _vm.SelectedChartRow;
    }

    // 为当前选中的 ChartInfo 行选择谱面文件（.txt/.spc）。
    private void BtnBrowseSelectedChart_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedChartRow == null)
        {
            MessageBox.Show("请先在表格中选中一条谱面项。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "导入谱面文件",
            Filter = "谱面文件 (*.spc;*.txt)|*.spc;*.txt|SPC 谱面 (*.spc)|*.spc|TXT 谱面 (*.txt)|*.txt"
        };
        if (dialog.ShowDialog() != true) return;

        _vm.SelectedChartRow.ChartFilePath = dialog.FileName;
        _vm.Status = $"已为槽位 {_vm.SelectedChartRow.ChartSlotIndex} 选择谱面：{Path.GetFileName(dialog.FileName)}";
    }

    // 从已有曲目复制“歌曲显示与行为字段”（优先曲绘模板同名曲目，找不到再手动选择）。
    private void BtnCopySongDisplaySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_bundleScan == null)
                throw new Exception("请先扫描 .bundle。");

            SongDatabaseSlotInfo? source = null;
            if (_vm.SelectedJacketTemplate != null)
            {
                source = _bundleScan.Slots.FirstOrDefault(x =>
                    !x.IsEmpty &&
                    !string.IsNullOrWhiteSpace(x.BaseName) &&
                    string.Equals(x.BaseName, _vm.SelectedJacketTemplate.BaseName, StringComparison.OrdinalIgnoreCase));
            }

            if (source == null)
            {
                source = ShowSongSourcePickerDialog(_bundleScan.Slots.Where(x => !x.IsEmpty).OrderBy(x => x.BaseName).ToList());
                if (source == null) return;
            }

            _vm.DisplayNameSectionIndicator = source.DisplayNameSectionIndicator ?? "";
            _vm.DisplayArtistSectionIndicator = source.DisplayArtistSectionIndicator ?? "";
            _vm.GameplayBackground = source.GameplayBackground;
            _vm.RewardStyle = source.RewardStyle;

            _vm.Status =
                $"已照抄曲目设置：{source.BaseName}\n" +
                $"DisplayNameSectionIndicator={_vm.DisplayNameSectionIndicator}, " +
                $"DisplayArtistSectionIndicator={_vm.DisplayArtistSectionIndicator}, " +
                $"GameplayBackground={_vm.GameplayBackground}, RewardStyle={_vm.RewardStyle}";
        }
        catch (Exception ex)
        {
            App.LogHandledException("打包谱面-照抄已有曲目设置", ex);
            MessageBox.Show($"照抄设置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 执行新增歌曲资源导出：写入 bundle(SongDatabase+曲绘)、assets(Mapping)并输出加密 BGM/SPC。
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 提交 DataGrid 当前编辑中的单元格，避免未离焦时值未写回 ViewModel。
            ChartRowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ChartRowsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var request = BuildRequestFromUi();
            _vm.Status = "正在导出新增歌曲资源，请稍候...";

            var result = UnitySongResourcePacker.ExportNewSongResources(request);

            var sb = new StringBuilder();
            sb.AppendLine(result.Summary);
            sb.AppendLine($"输出 bundle：{result.OutputBundlePath}");
            sb.AppendLine($"输出 sharedassets：{result.OutputSharedAssetsPath}");
            sb.AppendLine($"输出 resources.assets：{result.OutputResourcesAssetsPath}");
            sb.AppendLine($"新增曲绘 Texture2D PathID：{result.NewTexturePathId}");
            sb.AppendLine($"新增曲绘 Material PathID：{result.NewMaterialPathId}");
            sb.AppendLine("新增 Mapping 项：");
            foreach (var item in result.AddedMappingEntries)
                sb.AppendLine($"- {item.FullLookupPath} => {item.Guid} (Len={item.FileLength})");
            if (!string.IsNullOrWhiteSpace(result.DeploymentSummary))
            {
                sb.AppendLine("已写入游戏目录：");
                sb.AppendLine(result.DeploymentSummary);
            }

            _vm.Status = sb.ToString().TrimEnd();

            MessageBox.Show(
                $"新增歌曲资源导出成功。\n\nbundle：{Path.GetFileName(result.OutputBundlePath)}\nsharedassets：{Path.GetFileName(result.OutputSharedAssetsPath)}\nresources：{Path.GetFileName(result.OutputResourcesAssetsPath)}\n新增映射：{result.AddedMappingEntries.Count} 项",
                "成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.LogHandledException("打包谱面-导出", ex);
            string summary = BuildExceptionSummary(ex);
            string details = BuildExceptionDetails(ex);
            _vm.Status = $"导出失败：\n{details}";
            MessageBox.Show($"导出失败：\n{summary}\n\n详细信息已写入下方状态区。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 扫描当前 .bundle，并刷新空槽列表与曲绘模板列表。
    private void ReloadBundleScan()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_vm.BundleFilePath))
                throw new Exception("请先导入 .bundle 文件。");

            _vm.Status = "正在扫描 .bundle（SongDatabase 空槽 / 曲绘模板）...";

            int? prevSlot = _vm.SelectedSongSlot?.SlotIndex;
            long? prevTexPathId = _vm.SelectedJacketTemplate?.TexturePathId;

            _bundleScan = UnitySongResourcePacker.ScanBundle(_vm.BundleFilePath);

            _vm.EmptySongSlots.Clear();
            foreach (var slot in _bundleScan.Slots.Where(x => x.IsEmpty && x.SlotIndex >= 2))
                _vm.EmptySongSlots.Add(slot);

            _vm.JacketTemplates.Clear();
            foreach (var template in _bundleScan.JacketTemplates)
                _vm.JacketTemplates.Add(template);

            _vm.SelectedSongSlot = _vm.EmptySongSlots.FirstOrDefault(x => x.SlotIndex == prevSlot) ?? _vm.EmptySongSlots.FirstOrDefault();
            _vm.SelectedJacketTemplate = _vm.JacketTemplates.FirstOrDefault(x => x.TexturePathId == prevTexPathId) ?? _vm.JacketTemplates.FirstOrDefault();

            if (_vm.EmptySongSlots.Count == 0)
            {
                _vm.Status = "扫描完成，但未找到空槽（allSongInfo 中没有符合条件的空项）。";
                return;
            }

            if (_vm.JacketTemplates.Count == 0)
            {
                _vm.Status =
                    $"扫描完成：找到 {_bundleScan.Slots.Count} 个歌曲槽位，其中空槽 {_vm.EmptySongSlots.Count} 个；" +
                    "但未找到可用于复制的曲绘模板（需同名 Texture2D + Material）。";
                return;
            }

            _vm.Status =
                $"扫描完成：SongDatabase 位于 {_bundleScan.SongDatabaseAssetsFileName}，PathID={_bundleScan.SongDatabasePathId}。\n" +
                $"总槽位 {_bundleScan.Slots.Count} 个，空槽 {_vm.EmptySongSlots.Count} 个（已锁定保留槽 00/01，请手动选择）。\n" +
                $"可用曲绘模板 {_vm.JacketTemplates.Count} 个（同名 Texture2D + Material）。";
        }
        catch (Exception ex)
        {
            App.LogHandledException("打包谱面-扫描bundle", ex);
            _bundleScan = null;
            _vm.EmptySongSlots.Clear();
            _vm.JacketTemplates.Clear();
            _vm.SelectedSongSlot = null;
            _vm.SelectedJacketTemplate = null;
            _vm.Status = $"扫描 .bundle 失败：{ex.Message}";
            MessageBox.Show($"扫描 .bundle 失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 根据游戏目录自动定位 if-app_Data 下的 sharedassets0.assets 与目标 bundle。
    private void ApplyGameDirectory(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return;

        string root = Path.GetFullPath(gameDirectory);
        _vm.GameDirectory = root;

        string dataDir = Path.Combine(root, "if-app_Data");
        if (!Directory.Exists(dataDir))
        {
            _vm.BundleFilePath = "";
            _vm.SharedAssetsFilePath = "";
            _vm.ResourcesAssetsFilePath = "";
            _vm.Status = $"目录不符合预期结构：未找到 if-app_Data\n{root}";
            MessageBox.Show("未在该目录下找到 if-app_Data。\n请选择游戏根目录（例如 In Falsus Demo）。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string sharedAssets = Path.Combine(dataDir, "sharedassets0.assets");
        string resourcesAssets = Path.Combine(dataDir, "resources.assets");
        string bundleDir = Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64");
        string defaultBundle = Path.Combine(bundleDir, "3d6c628d95a26a13f4e5a73be91cb4f7.bundle");

        _vm.SharedAssetsFilePath = File.Exists(sharedAssets) ? sharedAssets : "";
        _vm.ResourcesAssetsFilePath = File.Exists(resourcesAssets) ? resourcesAssets : "";
        _vm.BundleFilePath = ResolveTargetSongBundlePath(bundleDir, defaultBundle) ?? "";

        _vm.OutputDirectory = Path.Combine(root, "SongData");

        var notes = new List<string>();
        if (!File.Exists(sharedAssets))
            notes.Add("未找到 if-app_Data\\sharedassets0.assets");
        if (!File.Exists(resourcesAssets))
            notes.Add("未找到 if-app_Data\\resources.assets");
        if (string.IsNullOrWhiteSpace(_vm.BundleFilePath))
            notes.Add("未找到目标歌曲数据库 bundle（默认 3d6c...bundle 及目录内 .bundle 已尝试）。");

        if (notes.Count > 0)
        {
            _bundleScan = null;
            _vm.EmptySongSlots.Clear();
            _vm.JacketTemplates.Clear();
            _vm.SelectedSongSlot = null;
            _vm.SelectedJacketTemplate = null;
            _vm.Status = "自动定位未完成：\n" + string.Join("\n", notes);
            return;
        }

        ReloadBundleScan();
    }

    // 在固定目录下优先定位指定 hash 的 bundle，找不到时回退到目录内第一个 bundle。
    private static string? ResolveTargetSongBundlePath(string bundleDir, string defaultBundlePath)
    {
        if (File.Exists(defaultBundlePath))
            return defaultBundlePath;

        if (!Directory.Exists(bundleDir))
            return null;

        return Directory.EnumerateFiles(bundleDir, "*.bundle", SearchOption.TopDirectoryOnly)
            .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    // 让用户从已有曲目中手动选择一首作为“显示与行为字段”的复制来源。
    private SongDatabaseSlotInfo? ShowSongSourcePickerDialog(IReadOnlyList<SongDatabaseSlotInfo> candidates)
    {
        if (candidates.Count == 0)
        {
            MessageBox.Show("当前 SongDatabase 中没有可供照抄的已有曲目。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var listBox = new ListBox
        {
            Margin = new Thickness(0, 8, 0, 8),
            MinWidth = 420,
            MinHeight = 260
        };
        foreach (var item in candidates)
            listBox.Items.Add(item);
        listBox.DisplayMemberPath = nameof(SongDatabaseSlotInfo.DisplayText);
        listBox.SelectedIndex = 0;

        var okButton = new Button { Content = "确定", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "取消", Width = 88, IsCancel = true };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = "未找到与当前曲绘模板同名的曲目，请手动选择一个已有曲目作为设置来源：",
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(listBox, Dock.Bottom);
        panel.Children.Add(listBox);

        var dialog = new Window
        {
            Owner = this,
            Title = "选择照抄来源曲目",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Content = panel
        };

        okButton.Click += (_, _) => dialog.DialogResult = true;

        return dialog.ShowDialog() == true ? listBox.SelectedItem as SongDatabaseSlotInfo : null;
    }

    // 根据当前界面输入构造后端导出请求。
    private NewSongPackRequest BuildRequestFromUi()
    {
        if (_bundleScan == null)
            throw new Exception("请先扫描 .bundle 并选择空槽与曲绘模板。");
        if (string.IsNullOrWhiteSpace(_vm.SharedAssetsFilePath))
            throw new Exception("请先导入 sharedassets0.assets。");
        if (string.IsNullOrWhiteSpace(_vm.ResourcesAssetsFilePath))
            throw new Exception("请先定位 resources.assets。");
        if (string.IsNullOrWhiteSpace(_vm.JacketImageFilePath))
            throw new Exception("请先导入曲绘。");
        if (string.IsNullOrWhiteSpace(_vm.BgmFilePath))
            throw new Exception("请先导入 BGM。");
        if (string.IsNullOrWhiteSpace(_vm.OutputDirectory))
            throw new Exception("请选择导出文件夹。");
        if (_vm.SelectedSongSlot == null)
            throw new Exception("请选择一个空槽。");
        if (_vm.SelectedJacketTemplate == null)
            throw new Exception("请选择一个曲绘模板。");

        var charts = BuildChartItemsFromRows();
        if (charts.Count == 0)
            throw new Exception("请至少启用并配置一个谱面项。");

        return new NewSongPackRequest
        {
            BundleFilePath = _vm.BundleFilePath,
            SharedAssetsFilePath = _vm.SharedAssetsFilePath,
            ResourcesAssetsFilePath = _vm.ResourcesAssetsFilePath,
            OutputDirectory = !string.IsNullOrWhiteSpace(_vm.GameDirectory)
                ? Path.Combine(_vm.GameDirectory, "SongData")
                : _vm.OutputDirectory,
            JacketImageFilePath = _vm.JacketImageFilePath,
            BgmFilePath = _vm.BgmFilePath,
            BaseName = (_vm.BaseName ?? "").Trim(),
            KeepJacketOriginalSize = _vm.KeepJacketOriginalSize,
            SelectedSlot = _vm.SelectedSongSlot,
            JacketTemplate = _vm.SelectedJacketTemplate,
            PreviewStartSeconds = _vm.PreviewStartSeconds,
            PreviewEndSeconds = _vm.PreviewEndSeconds,
            DisplayNameSectionIndicator = _vm.DisplayNameSectionIndicator ?? "",
            DisplayArtistSectionIndicator = _vm.DisplayArtistSectionIndicator ?? "",
            SongTitleEnglish = (_vm.SongTitleEnglish ?? "").Trim(),
            SongArtistEnglish = (_vm.SongArtistEnglish ?? "").Trim(),
            GameplayBackground = _vm.GameplayBackground,
            RewardStyle = _vm.RewardStyle,
            Charts = charts,
            AutoRenameWhenTargetLocked = _vm.AutoRenameWhenTargetLocked
        };
    }

    // 从表格行构造 ChartInfos 与谱面资源映射输入。
    private List<NewSongChartPackItem> BuildChartItemsFromRows()
    {
        var list = new List<NewSongChartPackItem>();
        foreach (var row in _vm.ChartRows)
        {
            if (!row.Enabled) continue;
            list.Add(new NewSongChartPackItem
            {
                ChartSlotIndex = row.ChartSlotIndex,
                SourceChartFilePath = row.ChartFilePath ?? "",
                DifficultyFlag = row.DifficultyFlag,
                Available = row.Available,
                Rating = row.Rating,
                LevelSectionIndicator = row.LevelSectionIndicator ?? "",
                DisplayChartDesigner = row.DisplayChartDesigner ?? "",
                DisplayJacketDesigner = row.DisplayJacketDesigner ?? "",
            });
        }
        return list;
    }

    // 生成一条默认谱面配置行，尽量避开已用槽位并给出常用 Difficulty。
    private BundleTexturePackageChartRowViewModel CreateDefaultChartRow()
    {
        int slot = GetNextAvailableChartSlotIndex();
        byte difficulty = slot switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            3 => 8,
            _ => 1
        };

        return new BundleTexturePackageChartRowViewModel
        {
            Enabled = true,
            ChartSlotIndex = slot,
            DifficultyFlag = difficulty,
            Available = 1,
            Rating = 1,
            LevelSectionIndicator = "1",
            DisplayChartDesigner = "",
            DisplayJacketDesigner = "",
            ChartFilePath = ""
        };
    }

    // 获取尚未被当前表格使用的第一个谱面槽位（0-3）。
    private int GetNextAvailableChartSlotIndex()
    {
        var used = _vm.ChartRows.Select(x => x.ChartSlotIndex).ToHashSet();
        for (int i = 0; i <= 3; i++)
        {
            if (!used.Contains(i)) return i;
        }
        return 0;
    }

    // 构建适合 MessageBox 展示的异常摘要（类型 + 消息 + 第一层内部异常 + 首帧堆栈）。
    private static string BuildExceptionSummary(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append(ex.GetType().Name);
        if (!string.IsNullOrWhiteSpace(ex.Message))
            sb.Append(": ").Append(ex.Message);

        if (ex.InnerException != null)
        {
            sb.Append("\nInner: ").Append(ex.InnerException.GetType().Name);
            if (!string.IsNullOrWhiteSpace(ex.InnerException.Message))
                sb.Append(": ").Append(ex.InnerException.Message);
        }

        string? firstStack = ex.StackTrace?
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstStack))
            sb.Append("\n").Append(firstStack);

        return sb.ToString();
    }

    // 构建详细异常文本，写入状态区便于截图定位。
    private static string BuildExceptionDetails(Exception ex)
    {
        var sb = new StringBuilder();
        int depth = 0;
        for (Exception? cur = ex; cur != null; cur = cur.InnerException, depth++)
        {
            if (depth > 0)
                sb.AppendLine().AppendLine("---- Inner Exception ----");

            sb.AppendLine($"{cur.GetType().FullName}: {cur.Message}");

            var stackLines = cur.StackTrace?
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Take(16);
            if (stackLines != null)
            {
                bool any = false;
                foreach (var line in stackLines)
                {
                    if (!any)
                    {
                        sb.AppendLine("Stack:");
                        any = true;
                    }
                    sb.AppendLine(line);
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

}

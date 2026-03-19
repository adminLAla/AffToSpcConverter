using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InFalsusSongPackStudio.Utils;
using InFalsusSongPackStudio.ViewModels;
using Microsoft.Win32;
using System.Text.Encodings.Web;
using System.IO.Compression;
using System.Text.Json;

namespace InFalsusSongPackStudio.Views;

// “打包谱面”窗口，负责新增歌曲资源的收集、校验与导出。
public partial class BundleTexturePackageWindow : Window
{
    private const int MaxChartRowCount = 4;
    private const double RightPanelExpandedWidth = 320;
    private const double ChartGridHeaderHeight = 30;
    private const double ChartGridRowHeight = 26;
    private const double ChartGridPaddingHeight = 6;
    private readonly BundleTexturePackageViewModel _vm = new();
    private SongBundleScanResult? _bundleScan;
    private bool _isRightPanelCollapsed;
    private bool _hasExportedCurrentSong;
    private int? _pendingZipExportSlotToLock;
    private readonly HashSet<int> _lockedSongSlotsAfterZipReset = new();

    // 初始化“打包谱面”窗口。
    public BundleTexturePackageWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.PropertyChanged += Vm_PropertyChanged;
        _vm.ChartRows.CollectionChanged += ChartRows_CollectionChanged;

        // 预置一条谱面项，减少首次使用时的操作步骤。
        if (_vm.ChartRows.Count == 0)
        {
            var row = CreateDefaultChartRow();
            _vm.ChartRows.Add(row);
            _vm.SelectedChartRow = row;
        }

        foreach (var row in _vm.ChartRows)
            row.PropertyChanged += ChartRow_PropertyChanged;

        ApplySavedGameDirectory();
        ApplyRightPanelState();

        UpdateOperationGuide();
    }

    private void ToggleRightPanel_Click(object sender, RoutedEventArgs e)
    {
        _isRightPanelCollapsed = !_isRightPanelCollapsed;
        ApplyRightPanelState();
    }

    private void ApplyRightPanelState()
    {
        if (_isRightPanelCollapsed)
        {
            RightPanel.Visibility = Visibility.Collapsed;
            RightPanelColumn.Width = new GridLength(0);
            Grid.SetColumn(BtnToggleRightPanel, 0);
            BtnToggleRightPanel.HorizontalAlignment = HorizontalAlignment.Right;
            BtnToggleRightPanel.Margin = new Thickness(0, 8, 26, 0);
            BtnToggleRightPanel.Content = "<";
            BtnToggleRightPanel.ToolTip = "展开右侧栏";
            ResetChartGridHorizontalOffset();
            return;
        }

        RightPanel.Visibility = Visibility.Visible;
        RightPanelColumn.Width = new GridLength(RightPanelExpandedWidth);
        Grid.SetColumn(BtnToggleRightPanel, 1);
        BtnToggleRightPanel.HorizontalAlignment = HorizontalAlignment.Right;
        BtnToggleRightPanel.Margin = new Thickness(0, 8, 8, 0);
        BtnToggleRightPanel.Content = "☰";
        BtnToggleRightPanel.ToolTip = "收起右侧栏";
        ResetChartGridHorizontalOffset();
    }

    private void ShowPrompt(string title, string message)
    {
        AppPromptDialog.Show(this, title, message);
    }

    private void ChartRowsGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ResetChartGridHorizontalOffset();
        UpdateChartGridHeight();
    }

    private void ResetChartGridHorizontalOffset()
    {
        if (ChartRowsGrid == null) return;

        var scroller = FindVisualChild<ScrollViewer>(ChartRowsGrid);
        scroller?.ScrollToHorizontalOffset(0);
    }

    private void FocusChartGridFirstColumn(object? rowItem = null)
    {
        if (ChartRowsGrid == null || ChartRowsGrid.Columns.Count == 0)
            return;

        object? targetRow = rowItem;
        if (targetRow == null)
            targetRow = ChartRowsGrid.SelectedItem;
        if (targetRow == null && ChartRowsGrid.Items.Count > 0)
            targetRow = ChartRowsGrid.Items[0];
        if (targetRow == null)
            return;

        ChartRowsGrid.CurrentCell = new DataGridCellInfo(targetRow, ChartRowsGrid.Columns[0]);
        ChartRowsGrid.ScrollIntoView(targetRow, ChartRowsGrid.Columns[0]);
        ResetChartGridHorizontalOffset();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;

            var found = FindVisualChild<T>(child);
            if (found != null)
                return found;
        }

        return null;
    }

    // 关闭窗口时主动释放持有的数据与事件订阅，降低内存驻留。
    protected override void OnClosed(EventArgs e)
    {
        try
        {
            ReleaseWindowResources();
        }
        finally
        {
            base.OnClosed(e);
        }
    }

    private void ReleaseWindowResources()
    {
        _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm.ChartRows.CollectionChanged -= ChartRows_CollectionChanged;

        foreach (var row in _vm.ChartRows)
            row.PropertyChanged -= ChartRow_PropertyChanged;

        _bundleScan = null;
        _vm.EmptySongSlots.Clear();
        _vm.JacketTemplates.Clear();
        _vm.ChartRows.Clear();
        _vm.SelectedSongSlot = null;
        _vm.SelectedJacketTemplate = null;
        _vm.SelectedChartRow = null;
        _vm.Status = "";
        _vm.OperationGuide = "";
        DataContext = null;

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BundleTexturePackageViewModel.GameDirectory)
            or nameof(BundleTexturePackageViewModel.BundleFilePath)
            or nameof(BundleTexturePackageViewModel.SharedAssetsFilePath)
            or nameof(BundleTexturePackageViewModel.ResourcesAssetsFilePath)
            or nameof(BundleTexturePackageViewModel.JacketImageFilePath)
            or nameof(BundleTexturePackageViewModel.BgmFilePath)
            or nameof(BundleTexturePackageViewModel.BaseName)
            or nameof(BundleTexturePackageViewModel.SongTitleEnglish)
            or nameof(BundleTexturePackageViewModel.SongArtistEnglish)
            or nameof(BundleTexturePackageViewModel.SelectedSongSlot)
            or nameof(BundleTexturePackageViewModel.SelectedJacketTemplate))
        {
            UpdateOperationGuide();
        }
    }

    private void ChartRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<BundleTexturePackageChartRowViewModel>())
                item.PropertyChanged -= ChartRow_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<BundleTexturePackageChartRowViewModel>())
                item.PropertyChanged += ChartRow_PropertyChanged;
        }

        UpdateChartGridHeight();
        UpdateOperationGuide();
    }

    private void UpdateChartGridHeight()
    {
        if (ChartRowsGrid == null) return;

        int rowCount = Math.Max(1, _vm.ChartRows.Count);
        double height = ChartGridHeaderHeight + rowCount * ChartGridRowHeight + ChartGridPaddingHeight;
        ChartRowsGrid.Height = height;
    }

    private void ChartRow_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BundleTexturePackageChartRowViewModel.Available)
            or nameof(BundleTexturePackageChartRowViewModel.IsAvailable)
            or nameof(BundleTexturePackageChartRowViewModel.ChartFilePath)
            or nameof(BundleTexturePackageChartRowViewModel.ChartSlotIndex)
            or nameof(BundleTexturePackageChartRowViewModel.DifficultyFlag))
        {
            UpdateOperationGuide();
        }
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
        UpdateOperationGuide();
    }

    // 选择新增歌曲 BGM（.ogg/.wav），导出时会按游戏约定写入 .wav 映射键。
    private void BtnBrowseBgm_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 BGM",
            Filter = "音频文件 (*.ogg;*.wav;*.mp3)|*.ogg;*.wav;*.mp3|Ogg 音频 (*.ogg)|*.ogg|Wav 音频 (*.wav)|*.wav|MP3 音频 (*.mp3)|*.mp3"
        };
        if (dialog.ShowDialog() != true) return;

        _vm.BgmFilePath = dialog.FileName;

        double bgmDurationSeconds;
        try
        {
            bgmDurationSeconds = UnitySongResourcePacker.ReadAudioDurationSeconds(dialog.FileName);
        }
        catch
        {
            _vm.Status = $"已导入 BGM：{Path.GetFileName(dialog.FileName)}（导出时会使用 .wav 映射键）。";
            UpdateOperationGuide();
            return;
        }

        const double defaultPreviewSpanSeconds = 15d;
        double start = _vm.PreviewStartSeconds;
        if (double.IsNaN(start) || double.IsInfinity(start) || start < 0)
            start = 0;
        if (start > bgmDurationSeconds)
            start = 0;

        double end = Math.Min(bgmDurationSeconds, start + defaultPreviewSpanSeconds);
        if (end < start)
            end = start;

        _vm.PreviewStartSeconds = start;
        _vm.PreviewEndSeconds = end;
        _vm.Status =
            $"已导入 BGM：{Path.GetFileName(dialog.FileName)}（导出时会使用 .wav 映射键）。\n" +
            $"试听区间已按曲长设置为 {start:0.###} ~ {end:0.###} 秒（曲长 {bgmDurationSeconds:0.###} 秒，默认跨度 15 秒）。";
        UpdateOperationGuide();
    }

    // 选择导出目录，默认推荐游戏根目录下 SongData。
    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择导出目录"
        };

        if (dialog.ShowDialog() != true) return;

        _vm.OutputDirectory = dialog.FolderName;
        _vm.Status = $"已设置输出目录：{_vm.OutputDirectory}";
        UpdateOperationGuide();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        ResetForNextSong();
    }

    private void ResetForNextSong()
    {
        ConsumePendingZipExportSlotLock();
        _hasExportedCurrentSong = false;
        _vm.BaseName = string.Empty;
        _vm.SongTitleEnglish = string.Empty;
        _vm.SongArtistEnglish = string.Empty;
        _vm.DisplayNameSectionIndicator = "A";
        _vm.DisplayArtistSectionIndicator = "A";
        _vm.GameplayBackground = 3;
        _vm.RewardStyle = 0;
        _vm.JacketImageFilePath = string.Empty;
        _vm.BgmFilePath = string.Empty;
        _vm.PreviewStartSeconds = 0;
        _vm.PreviewEndSeconds = 15;
        _vm.KeepJacketOriginalSize = true;

        _vm.ChartRows.Clear();
        var row = CreateDefaultChartRow();
        _vm.ChartRows.Add(row);
        _vm.SelectedChartRow = row;
        ChartRowsGrid.SelectedItem = row;
        FocusChartGridFirstColumn(row);

        // 重置后强制重扫资源，确保批量导出/恢复后的槽位占用状态能立即反映到下拉框。
        ReloadBundleScan();

        // 重置后优先选择最前面的空槽，便于连续导入下一首。
        _vm.SelectedSongSlot = _vm.EmptySongSlots.FirstOrDefault(x => x.IsEmpty)
            ?? _vm.EmptySongSlots.FirstOrDefault();

        // 部分控件更新会在布局阶段改变滚动位置；这里在空闲优先级再次滚到顶部。
        MainScrollViewer?.ScrollToTop();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            MainScrollViewer?.ScrollToTop();
        }));

        _vm.Status = "已重置当前歌曲表单并刷新资源状态，可继续导入下一首。";
        UpdateOperationGuide();
    }

    private void ConsumePendingZipExportSlotLock()
    {
        if (!_pendingZipExportSlotToLock.HasValue)
            return;

        int slotIndex = _pendingZipExportSlotToLock.Value;
        _pendingZipExportSlotToLock = null;

        if (!_lockedSongSlotsAfterZipReset.Add(slotIndex))
            return;

        var target = _vm.EmptySongSlots.FirstOrDefault(x => x.SlotIndex == slotIndex);
        if (target != null)
            _vm.EmptySongSlots.Remove(target);

        if (_vm.SelectedSongSlot?.SlotIndex == slotIndex)
            _vm.SelectedSongSlot = _vm.EmptySongSlots.FirstOrDefault();

        _vm.Status = $"已重置，并锁定上次导出曲包使用的槽位 [{slotIndex:00}]，本轮不再提供。";
    }

    // 重新扫描 .bundle，读取 SongDatabase 空槽和可作为模板的曲绘资源。
    private void BtnRescanBundle_Click(object sender, RoutedEventArgs e)
    {
        ReloadBundleScan();
    }

    // 添加一条 ChartInfo 配置行（对应一个谱面分档）。
    private void BtnAddChartRow_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ChartRows.Count >= MaxChartRowCount)
        {
            ShowPrompt("提示", $"谱面项最多只能添加 {MaxChartRowCount} 个。");
            return;
        }

        var row = CreateDefaultChartRow();
        _vm.ChartRows.Add(row);
        _vm.SelectedChartRow = row;
        ChartRowsGrid.SelectedItem = row;
        FocusChartGridFirstColumn(row);
    }

    // 删除当前选中的 ChartInfo 配置行。
    private void BtnRemoveChartRow_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedChartRow == null)
        {
            ShowPrompt("提示", "请先在表格中选中一条谱面项。");
            return;
        }

        _vm.ChartRows.Remove(_vm.SelectedChartRow);
        _vm.SelectedChartRow = _vm.ChartRows.LastOrDefault();
        ChartRowsGrid.SelectedItem = _vm.SelectedChartRow;
        FocusChartGridFirstColumn(_vm.SelectedChartRow);
        UpdateOperationGuide();
    }

    // 为当前选中的 ChartInfo 行选择谱面文件（.spc）。
    private void BtnBrowseSelectedChart_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedChartRow == null)
        {
            ShowPrompt("提示", "请先在表格中选中一条谱面项。");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "导入谱面文件",
            Filter = "SPC 谱面 (*.spc)|*.spc"
        };
        if (dialog.ShowDialog() != true) return;

        _vm.SelectedChartRow.ChartFilePath = dialog.FileName;
        _vm.Status = $"已为槽位 {_vm.SelectedChartRow.ChartSlotIndex} 选择谱面：{Path.GetFileName(dialog.FileName)}";
        UpdateOperationGuide();
    }

    // 当鼠标位于 ChartInfos DataGrid 上时，将滚轮滚动转发给外层页面 ScrollViewer。
    private void ChartRowsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ForwardWheelToMainScrollViewer(e);
    }

    // 将滚轮增量统一转发到外层页面滚动容器。
    private void ForwardWheelToMainScrollViewer(MouseWheelEventArgs e)
    {
        if (MainScrollViewer == null) return;

        double targetOffset = MainScrollViewer.VerticalOffset - e.Delta;
        if (targetOffset < 0) targetOffset = 0;
        if (targetOffset > MainScrollViewer.ScrollableHeight) targetOffset = MainScrollViewer.ScrollableHeight;

        MainScrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
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
            ShowPrompt("错误", $"照抄设置失败：{ex.Message}");
        }
    }

    private void BtnImportPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "导入曲包 ZIP",
                Filter = "ZIP 文件 (*.zip)|*.zip"
            };
            if (dialog.ShowDialog() != true) return;

            string zipPath = dialog.FileName;
            string zipDir = Path.GetDirectoryName(zipPath) ?? "";
            string zipNameWithoutExt = Path.GetFileNameWithoutExtension(zipPath);
            string extractDir = Path.Combine(zipDir, zipNameWithoutExt);

            // 解压到目标文件夹（若已存在则覆盖）
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, true);
            }
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // 读取song.json
            string songJsonPath = Path.Combine(extractDir, "song.json");
            if (!File.Exists(songJsonPath))
            {
                ShowPrompt("错误", "曲包内未找到 song.json。");
                return;
            }

            string json = File.ReadAllText(songJsonPath, Encoding.UTF8);
            var songInfo = JsonSerializer.Deserialize<JsonElement>(json);

            // 显示到UI，所有资源路径为解压目录下的绝对路径
            _vm.BaseName = songInfo.GetProperty("BaseName").GetString() ?? "";
            _vm.KeepJacketOriginalSize = songInfo.TryGetProperty("KeepJacketOriginalSize", out var keepJacket) && keepJacket.GetBoolean();
            _vm.PreviewStartSeconds = songInfo.TryGetProperty("PreviewStartSeconds", out var previewStart) ? previewStart.GetDouble() : 0;
            _vm.PreviewEndSeconds = songInfo.TryGetProperty("PreviewEndSeconds", out var previewEnd) ? previewEnd.GetDouble() : 15;
            _vm.DisplayNameSectionIndicator = songInfo.TryGetProperty("DisplayNameSectionIndicator", out var dnsi) ? dnsi.GetString() ?? "" : "";
            _vm.DisplayArtistSectionIndicator = songInfo.TryGetProperty("DisplayArtistSectionIndicator", out var dasi) ? dasi.GetString() ?? "" : "";
            _vm.SongTitleEnglish = songInfo.TryGetProperty("SongTitleEnglish", out var ste) ? ste.GetString() ?? "" : "";
            _vm.SongArtistEnglish = songInfo.TryGetProperty("SongArtistEnglish", out var sae) ? sae.GetString() ?? "" : "";
            _vm.GameplayBackground = songInfo.TryGetProperty("GameplayBackground", out var gb) ? gb.GetInt32() : 3;
            _vm.RewardStyle = songInfo.TryGetProperty("RewardStyle", out var rs) ? rs.GetInt32() : 0;
            _vm.AutoRenameWhenTargetLocked = songInfo.TryGetProperty("AutoRenameWhenTargetLocked", out var autoRename) && autoRename.GetBoolean();

            // 曲绘/BGM绝对路径
            var jacketFileName = songInfo.TryGetProperty("JacketImageFileName", out var jacket) ? jacket.GetString() : "";
            var bgmFileName = songInfo.TryGetProperty("BgmFileName", out var bgm) ? bgm.GetString() : "";
            _vm.JacketImageFilePath = !string.IsNullOrWhiteSpace(jacketFileName) ? Path.Combine(extractDir, jacketFileName) : "";
            _vm.BgmFilePath = !string.IsNullOrWhiteSpace(bgmFileName) ? Path.Combine(extractDir, bgmFileName) : "";

            // 谱面列表
            _vm.ChartRows.Clear();
            if (songInfo.TryGetProperty("Charts", out var charts) && charts.ValueKind == JsonValueKind.Array)
            {
                foreach (var chart in charts.EnumerateArray())
                {
                    var chartFileName = chart.TryGetProperty("SourceChartFileName", out var chartFile) ? chartFile.GetString() ?? "" : "";
                    var row = new BundleTexturePackageChartRowViewModel
                    {
                        ChartSlotIndex = chart.TryGetProperty("ChartSlotIndex", out var idx) ? idx.GetInt32() : 0,
                        DifficultyFlag = chart.TryGetProperty("DifficultyFlag", out var diff) && diff.TryGetByte(out var diffValue) ? diffValue : (byte)1,
                        Available = chart.TryGetProperty("Available", out var avail) ? avail.GetByte() : (byte)1,
                        Rating = chart.TryGetProperty("Rating", out var rating) ? rating.GetInt32() : 1,
                        LevelSectionIndicator = chart.TryGetProperty("LevelSectionIndicator", out var lvl) ? lvl.GetString() ?? "1" : "1",
                        DisplayChartDesigner = chart.TryGetProperty("DisplayChartDesigner", out var designer) ? designer.GetString() ?? "" : "",
                        DisplayJacketDesigner = chart.TryGetProperty("DisplayJacketDesigner", out var jacketDesigner) ? jacketDesigner.GetString() ?? "" : "",
                        ChartFilePath = !string.IsNullOrWhiteSpace(chartFileName) ? Path.Combine(extractDir, chartFileName) : ""
                    };
                    _vm.ChartRows.Add(row);
                }
            }

            int preferredSlotIndex = TryReadPreferredSlotIndex(songInfo);
            if (preferredSlotIndex >= 2)
            {
                if (_vm.EmptySongSlots.Count == 0 && !string.IsNullOrWhiteSpace(_vm.BundleFilePath) && File.Exists(_vm.BundleFilePath))
                    ReloadBundleScan();

                var preferredSlot = _vm.EmptySongSlots.FirstOrDefault(x => x.SlotIndex == preferredSlotIndex);
                if (preferredSlot != null)
                    _vm.SelectedSongSlot = preferredSlot;
            }

            _vm.Status = $"曲包已解压到：{extractDir}\n曲绘：{_vm.JacketImageFilePath}\nBGM：{_vm.BgmFilePath}\n谱面数：{_vm.ChartRows.Count}";
            if (preferredSlotIndex >= 2)
            {
                if (_vm.SelectedSongSlot?.SlotIndex == preferredSlotIndex)
                    _vm.Status += $"\n已回填曲包槽位：{preferredSlotIndex}";
                else
                    _vm.Status += $"\n曲包请求槽位：{preferredSlotIndex}（当前资源中未找到同槽位，请手动选择）";
            }
            UpdateOperationGuide();

            ShowPrompt("成功", "曲包导入并解压成功。");
        }
        catch (Exception ex)
        {
            App.LogHandledException("导入曲包", ex);
            string summary = BuildExceptionSummary(ex);
            string details = BuildExceptionDetails(ex);
            _vm.Status = $"导入失败：\n{details}";
            UpdateOperationGuide();
            ShowPrompt("错误", $"导入失败：\n{summary}\n\n详细信息已写入下方状态区。");
        }
    }


    private void BtnExportPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ChartRowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ChartRowsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var request = BuildRequestFromUi();

            // 弹出保存zip的对话框
            var dialog = new SaveFileDialog
            {
                Title = "导出曲包为 ZIP",
                Filter = "ZIP 文件 (*.zip)|*.zip",
                FileName = $"{request.BaseName}.zip"
            };
            if (dialog.ShowDialog() != true) return;

            // 若目标 zip 已存在，先删除以允许覆盖
            if (File.Exists(dialog.FileName))
            {
                File.Delete(dialog.FileName);
            }

            // 构造导出对象，过滤掉指定属性，并将 path 属性转为文件名
            var exportObj = new
            {
                JacketImageFileName = Path.GetFileName(request.JacketImageFilePath),
                BgmFileName = Path.GetFileName(request.BgmFilePath),
                BaseName = request.BaseName,
                KeepJacketOriginalSize = request.KeepJacketOriginalSize,
                SelectedSlotIndex = request.SelectedSlot.SlotIndex,
                SelectedSlot = request.SelectedSlot,
                JacketTemplate = request.JacketTemplate,
                PreviewStartSeconds = request.PreviewStartSeconds,
                PreviewEndSeconds = request.PreviewEndSeconds,
                DisplayNameSectionIndicator = request.DisplayNameSectionIndicator,
                DisplayArtistSectionIndicator = request.DisplayArtistSectionIndicator,
                SongTitleEnglish = request.SongTitleEnglish,
                SongArtistEnglish = request.SongArtistEnglish,
                GameplayBackground = request.GameplayBackground,
                RewardStyle = request.RewardStyle,
                Charts = request.Charts.Select(c => new
                {
                    SourceChartFileName = Path.GetFileName(c.SourceChartFilePath),
                    c.ChartSlotIndex,
                    c.Available,
                    c.DifficultyFlag,
                    c.DisplayChartDesigner,
                    c.DisplayJacketDesigner,
                    c.Rating,
                    c.LevelSectionIndicator
                }).ToList(),
                AutoRenameWhenTargetLocked = request.AutoRenameWhenTargetLocked
            };

            string json = System.Text.Json.JsonSerializer.Serialize(exportObj, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            // 打包到zip
            using (var zip = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create))
            {
                // 写入json到zip
                var jsonEntry = zip.CreateEntry("song.json", CompressionLevel.Optimal);
                using (var entryStream = jsonEntry.Open())
                using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                {
                    writer.Write(json);
                }

                // 添加曲绘
                if (File.Exists(request.JacketImageFilePath))
                    zip.CreateEntryFromFile(request.JacketImageFilePath, Path.GetFileName(request.JacketImageFilePath), CompressionLevel.Optimal);

                // 添加BGM
                if (File.Exists(request.BgmFilePath))
                    zip.CreateEntryFromFile(request.BgmFilePath, Path.GetFileName(request.BgmFilePath), CompressionLevel.Optimal);

                // 添加谱面
                foreach (var chart in request.Charts)
                {
                    if (!string.IsNullOrWhiteSpace(chart.SourceChartFilePath) && File.Exists(chart.SourceChartFilePath))
                    {
                        zip.CreateEntryFromFile(chart.SourceChartFilePath, Path.GetFileName(chart.SourceChartFilePath), CompressionLevel.Optimal);
                    }
                }
            }

            _vm.Status = $"已成功导出曲包到：{dialog.FileName}";
            _hasExportedCurrentSong = true;
            _pendingZipExportSlotToLock = request.SelectedSlot.SlotIndex;
            UpdateOperationGuide();

            ShowPrompt("成功", $"导出成功：\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            App.LogHandledException("打包谱面-导出ZIP", ex);
            string summary = BuildExceptionSummary(ex);
            string details = BuildExceptionDetails(ex);
            _vm.Status = $"导出失败：\n{details}";
            UpdateOperationGuide();
            ShowPrompt("错误", $"导出失败：\n{summary}\n\n详细信息已写入下方状态区。");
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
            _hasExportedCurrentSong = true;
            UpdateOperationGuide();

            ShowPrompt(
                "成功",
                $"新增歌曲资源导出成功。\n\nbundle：{Path.GetFileName(result.OutputBundlePath)}\nsharedassets：{Path.GetFileName(result.OutputSharedAssetsPath)}\nresources：{Path.GetFileName(result.OutputResourcesAssetsPath)}\n新增映射：{result.AddedMappingEntries.Count} 项");
        }
        catch (Exception ex)
        {
            App.LogHandledException("打包谱面-导出", ex);
            string summary = BuildExceptionSummary(ex);
            string details = BuildExceptionDetails(ex);
            _vm.Status = $"导出失败：\n{details}";
            UpdateOperationGuide();
            ShowPrompt("错误", $"导出失败：\n{summary}\n\n详细信息已写入下方状态区。");
        }
    }

    // 根据当前表单完成度更新右侧“操作步骤”提示。
    private void UpdateOperationGuide()
    {
        bool hasGameDirectory = !string.IsNullOrWhiteSpace(_vm.GameDirectory);
        bool hasJacket = !string.IsNullOrWhiteSpace(_vm.JacketImageFilePath);
        bool hasBgm = !string.IsNullOrWhiteSpace(_vm.BgmFilePath);
        bool hasSlotAndTemplate = _vm.SelectedSongSlot != null && _vm.SelectedJacketTemplate != null;
        bool hasBasicMeta =
            !string.IsNullOrWhiteSpace(_vm.BaseName) &&
            !string.IsNullOrWhiteSpace(_vm.SongTitleEnglish) &&
            !string.IsNullOrWhiteSpace(_vm.SongArtistEnglish);
        bool hasAtLeastOneChart = _vm.ChartRows.Any(x => !string.IsNullOrWhiteSpace(x.ChartFilePath));

        var steps = new List<(bool done, string text)>
        {
            (hasGameDirectory, "在设置中配置游戏目录（In Falsus Demo）"),
            (hasSlotAndTemplate, "确认空槽与曲绘模板（必要时点“扫描.bundle”）"),
            (hasJacket, "导入曲绘"),
            (hasBgm, "导入 BGM"),
            (hasBasicMeta, "填写 BaseName / 曲名(English) / 曲师(English)"),
            (hasAtLeastOneChart, "至少配置 1 条谱面并选择谱面文件")
        };

        string? nextStep = steps.FirstOrDefault(x => !x.done).text;
        if (string.IsNullOrWhiteSpace(nextStep))
            nextStep = "所有必填步骤已完成，可点击“导出”。";

        var sb = new StringBuilder();
        sb.AppendLine("请按顺序完成以下操作：");
        for (int i = 0; i < steps.Count; i++)
        {
            string mark = steps[i].done ? "✅" : "⬜";
            sb.AppendLine($"{mark} {i + 1}. {steps[i].text}");
        }

        sb.AppendLine();
        sb.AppendLine("可选操作：");
        sb.AppendLine("- 点击“照抄曲绘模板对应曲目的设置”快速填充 4 个显示/行为字段。" );
        sb.AppendLine("- 点击“导出为曲包”可生成单曲 ZIP，后续可用于批量打包导入。" );
        sb.AppendLine("- 批量处理多首歌曲时，可进入“批量打包”：先批量导入 ZIP，再批量导出到游戏目录。" );
        sb.AppendLine("- 若资源定位异常，可在设置中调整游戏目录后重新扫描 .bundle。" );
        if (_lockedSongSlotsAfterZipReset.Count > 0)
            sb.AppendLine($"- 已锁定槽位（重置后不再提供）：{string.Join(", ", _lockedSongSlotsAfterZipReset.OrderBy(x => x).Select(x => $"[{x:00}]"))}" );
        if (_pendingZipExportSlotToLock.HasValue)
            sb.AppendLine($"- 当前已记录待锁定槽位：[{_pendingZipExportSlotToLock.Value:00}]。点击“重置”后将从可选槽位中移除。" );
        if (_hasExportedCurrentSong)
            sb.AppendLine("- 当前歌曲已导出完成；若要导入下一首，点击下方“重置”即可开始下一轮。" );
        sb.AppendLine();
        sb.AppendLine($"下一步：{nextStep}");
        _vm.OperationGuide = sb.ToString().TrimEnd();
    }

    private void ApplySavedGameDirectory()
    {
        var settings = AppGlobalSettingsStore.Load();
        _vm.AutoRenameWhenTargetLocked = settings.AutoRenameWhenTargetLocked;

        string gameDirectory = (settings.GameDirectory ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            _vm.GameDirectory = string.Empty;
            _vm.BundleFilePath = string.Empty;
            _vm.SharedAssetsFilePath = string.Empty;
            _vm.ResourcesAssetsFilePath = string.Empty;
            _vm.OutputDirectory = string.Empty;
            _vm.Status = "未配置全局游戏目录，请在设置中先选择游戏根目录。";
            ResourceLocateHintText.Text = "资源文件尚未定位，请先在设置中配置有效游戏目录。";
            return;
        }

        ApplyGameDirectory(gameDirectory);
    }

    // 供主壳窗口在“设置已应用”后主动刷新。
    public void RefreshFromGlobalSettings()
    {
        ApplySavedGameDirectory();
    }

    // 扫描当前 .bundle，并刷新空槽列表与曲绘模板列表。
    private void ReloadBundleScan()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_vm.BundleFilePath))
                throw new Exception("请先导入 .bundle 文件。");

            _vm.Status = "正在扫描 .bundle（SongDatabase 槽位 / 曲绘模板）...";

            int? prevSlot = _vm.SelectedSongSlot?.SlotIndex;
            long? prevTexPathId = _vm.SelectedJacketTemplate?.TexturePathId;

            _bundleScan = UnitySongResourcePacker.ScanBundle(_vm.BundleFilePath);

            // 1. 显示所有槽位（不再只筛选空槽）
            _vm.EmptySongSlots.Clear();
            foreach (var slot in _bundleScan.Slots.Where(x => x.SlotIndex >= 2))
            {
                if (_lockedSongSlotsAfterZipReset.Contains(slot.SlotIndex))
                    continue;

                // 格式: [slot_id]<basename>(id=_id,Charts=_charts)
                string basename = string.IsNullOrWhiteSpace(slot.BaseName) ? "空槽" : slot.BaseName;
                //slot.DisplayText = $"[{slot.SlotIndex:D2}]{basename}(id={slot.SongIdValue},Charts={slot.ChartCount})";
                _vm.EmptySongSlots.Add(slot);
            }

            // 2. 曲绘模板逻辑不变
            _vm.JacketTemplates.Clear();
            foreach (var template in _bundleScan.JacketTemplates)
                _vm.JacketTemplates.Add(template);

            // 3. 优先选中空槽，否则保留原有选择
            var emptySlot = _vm.EmptySongSlots.FirstOrDefault(x => x.IsEmpty && x.SlotIndex == prevSlot)
                            ?? _vm.EmptySongSlots.FirstOrDefault(x => x.IsEmpty)
                            ?? _vm.EmptySongSlots.FirstOrDefault(x => x.SlotIndex == prevSlot)
                            ?? _vm.EmptySongSlots.FirstOrDefault();
            _vm.SelectedSongSlot = emptySlot;

            _vm.SelectedJacketTemplate = _vm.JacketTemplates.FirstOrDefault(x => x.TexturePathId == prevTexPathId)
                                         ?? _vm.JacketTemplates.FirstOrDefault();

            if (_vm.EmptySongSlots.Count == 0)
            {
                _vm.Status = "扫描完成，但未找到可用槽位（allSongInfo 中没有符合条件的项）。";
                return;
            }

            if (_vm.JacketTemplates.Count == 0)
            {
                _vm.Status =
                    $"扫描完成：找到 {_bundleScan.Slots.Count} 个歌曲槽位；" +
                    "但未找到可用于复制的曲绘模板（需同名 Texture2D + Material）。";
                return;
            }

            int usedCount = _bundleScan.Slots.Count(x => !x.IsEmpty && x.SlotIndex >= 2);
            int emptyCount = _bundleScan.Slots.Count(x => x.IsEmpty && x.SlotIndex >= 2);

            _vm.Status =
                $"扫描完成：SongDatabase 位于 {_bundleScan.SongDatabaseAssetsFileName}，PathID={_bundleScan.SongDatabasePathId}。\n" +
                $"总槽位 {_bundleScan.Slots.Count} 个，空槽 {emptyCount} 个，已占用 {usedCount} 个（已锁定保留槽 00/01，请手动选择）。\n" +
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
            ShowPrompt("错误", $"扫描 .bundle 失败：{ex.Message}");
        }
    }

    // 根据游戏目录自动定位 if-app_Data 下的 sharedassets0.assets 与目标 bundle。
    private void ApplyGameDirectory(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return;

        string root = Path.GetFullPath(gameDirectory);
        string previousGameDirectory = _vm.GameDirectory;
        string previousOutputDirectory = _vm.OutputDirectory;

        _vm.GameDirectory = root;

        string dataDir = Path.Combine(root, "if-app_Data");
        if (!Directory.Exists(dataDir))
        {
            _vm.BundleFilePath = "";
            _vm.SharedAssetsFilePath = "";
            _vm.ResourcesAssetsFilePath = "";
            _vm.Status = $"目录不符合预期结构：未找到 if-app_Data\n{root}";
            ResourceLocateHintText.Text = "资源文件尚未定位：未找到 if-app_Data，请在设置中选择正确游戏目录。";
            ShowPrompt("错误", "未在该目录下找到 if-app_Data。\n请选择游戏根目录（例如 In Falsus Demo）。");
            return;
        }

        string sharedAssets = Path.Combine(dataDir, "sharedassets0.assets");
        string resourcesAssets = Path.Combine(dataDir, "resources.assets");
        string bundleDir = Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64");
        string defaultBundle = Path.Combine(bundleDir, "3d6c628d95a26a13f4e5a73be91cb4f7.bundle");

        _vm.SharedAssetsFilePath = File.Exists(sharedAssets) ? sharedAssets : "";
        _vm.ResourcesAssetsFilePath = File.Exists(resourcesAssets) ? resourcesAssets : "";
        _vm.BundleFilePath = ResolveTargetSongBundlePath(bundleDir, defaultBundle) ?? "";

        string newDefaultOutputDirectory = Path.Combine(root, "SongData");
        string previousDefaultOutputDirectory = string.IsNullOrWhiteSpace(previousGameDirectory)
            ? string.Empty
            : Path.Combine(previousGameDirectory, "SongData");

        bool shouldResetToDefaultOutput =
            string.IsNullOrWhiteSpace(previousOutputDirectory) ||
            string.Equals(previousOutputDirectory, previousDefaultOutputDirectory, StringComparison.OrdinalIgnoreCase);

        if (shouldResetToDefaultOutput)
            _vm.OutputDirectory = newDefaultOutputDirectory;

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
            ResourceLocateHintText.Text = "资源文件定位未完成，请检查设置中的游戏目录。";
            return;
        }

        ResourceLocateHintText.Text = "资源文件已成功自动定位（.bundle / sharedassets0.assets / resources.assets）。";
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
            ShowPrompt("提示", "当前 SongDatabase 中没有可供照抄的已有曲目。");
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
    // 修改 BuildRequestFromUi，允许覆盖导入
    private NewSongPackRequest BuildRequestFromUi()
    {
        if (_vm.ChartRows.Count > MaxChartRowCount)
            throw new Exception($"谱面项数量超出限制，最多允许 {MaxChartRowCount} 个。");

        if (_bundleScan == null)
            throw new Exception("请先扫描 .bundle 并选择槽位与曲绘模板。");
        if (string.IsNullOrWhiteSpace(_vm.SharedAssetsFilePath))
            throw new Exception("未找到 sharedassets0.assets，请检查设置中的游戏目录。");
        if (string.IsNullOrWhiteSpace(_vm.ResourcesAssetsFilePath))
            throw new Exception("未找到 resources.assets，请检查设置中的游戏目录。");
        if (string.IsNullOrWhiteSpace(_vm.JacketImageFilePath))
            throw new Exception("请先导入曲绘。");
        if (string.IsNullOrWhiteSpace(_vm.BgmFilePath))
            throw new Exception("请先导入 BGM。");
        if (string.IsNullOrWhiteSpace(_vm.OutputDirectory))
            throw new Exception("未配置输出目录，请先在设置中配置有效游戏目录。");
        if (_vm.SelectedSongSlot == null)
            throw new Exception("请选择一个槽位。"); // 允许任意槽位
        if (_vm.SelectedJacketTemplate == null)
            throw new Exception("请选择一个曲绘模板。");

        var charts = BuildChartItemsFromRows();
        if (charts.Count > MaxChartRowCount)
            throw new Exception($"谱面项数量超出限制，最多允许 {MaxChartRowCount} 个。");
        if (charts.Count == 0)
            throw new Exception("请至少配置一个谱面项并选择谱面文件。");

        return new NewSongPackRequest
        {
            BundleFilePath = _vm.BundleFilePath,
            SharedAssetsFilePath = _vm.SharedAssetsFilePath,
            ResourcesAssetsFilePath = _vm.ResourcesAssetsFilePath,
            OutputDirectory = !string.IsNullOrWhiteSpace(_vm.OutputDirectory)
                ? _vm.OutputDirectory
                : (!string.IsNullOrWhiteSpace(_vm.GameDirectory)
                    ? Path.Combine(_vm.GameDirectory, "SongData")
                    : string.Empty),
            JacketImageFilePath = _vm.JacketImageFilePath,
            BgmFilePath = _vm.BgmFilePath,
            BaseName = (_vm.BaseName ?? "").Trim(),
            KeepJacketOriginalSize = _vm.KeepJacketOriginalSize,
            SelectedSlot = _vm.SelectedSongSlot, // 允许覆盖
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
            if (string.IsNullOrWhiteSpace(row.ChartFilePath)) continue;
            if (!BundleTexturePackageChartRowViewModel.IsSupportedDifficultyFlag(row.DifficultyFlag))
                throw new Exception($"谱面槽位 {row.ChartSlotIndex} 的 Difficulty 仅支持 1/2/4/8，当前为 {row.DifficultyFlag}。请先修正红框项。");

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

    private static int TryReadPreferredSlotIndex(JsonElement songInfo)
    {
        if (songInfo.TryGetProperty("SelectedSlotIndex", out var slotIndexJson) && slotIndexJson.TryGetInt32(out int directSlot))
            return directSlot;

        if (songInfo.TryGetProperty("SelectedSlot", out var slotJson)
            && slotJson.ValueKind == JsonValueKind.Object
            && slotJson.TryGetProperty("SlotIndex", out var nestedSlot)
            && nestedSlot.TryGetInt32(out int nestedSlotIndex))
        {
            return nestedSlotIndex;
        }

        return -1;
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
        if (_vm.ChartRows.Count == 0)
            return 0;

        int maxUsed = _vm.ChartRows.Max(x => x.ChartSlotIndex);
        int next = maxUsed + 1;
        return next <= 3 ? next : 3;
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

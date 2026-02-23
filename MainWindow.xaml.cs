using AffToSpcConverter.Convert;
using AffToSpcConverter.Convert.Preview;
using AffToSpcConverter.IO;
using AffToSpcConverter.Parsing;
using AffToSpcConverter.Utils;
using AffToSpcConverter.ViewModels;
using AffToSpcConverter.Views;
using AffToSpcConverter.Models;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Vorbis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AffToSpcConverter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    // ===== 音频播放（NAudio）=====
    private IWavePlayer? _waveOut;
    private WaveStream? _audioStream;
    private bool _renderHooked = false;

    // ===== 属性面板状态 =====
    private RenderItem? _selectedRenderItem;

    private Views.AddNoteRequest? _pendingAddRequest;

    // 通用数字字段：(字段名, TextBox)
    private readonly List<(string fieldName, TextBox textBox)> _propFields = new();
    // 下拉框字段：(字段名, ComboBox) — 用于枚举/有限选项
    private readonly List<(string fieldName, ComboBox comboBox)> _propComboFields = new();
    // 预览编辑历史：撤回栈（存放操作前快照）。
    private readonly Stack<List<ISpcEvent>> _undoEventHistory = new();
    // 预览编辑历史：恢复栈（存放被撤回的快照）。
    private readonly Stack<List<ISpcEvent>> _redoEventHistory = new();

    // 初始化主窗口并绑定预览事件。
    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        // 绑定音符选中事件
        PreviewControl.NoteSelected += OnNoteSelected;
        PreviewControl.NoteClicked += OnPreviewNoteClicked;
        PreviewControl.NotePlacementCommitted += OnNotePlacementCommitted;
        RefreshPreviewEditButtonStates();
    }

    // 根据当前选中状态与撤回/恢复历史更新顶部编辑按钮可用性。
    private void RefreshPreviewEditButtonStates()
    {
        bool hasSelectedEditableNote = _selectedRenderItem?.SourceEvent != null;
        BtnDeleteNoteTop.IsEnabled = hasSelectedEditableNote;
        BtnEditNoteTop.IsEnabled = hasSelectedEditableNote;
        BtnUndoTop.IsEnabled = _undoEventHistory.Count > 0;
        BtnRedoTop.IsEnabled = _redoEventHistory.Count > 0;
    }

    // ===== 文件操作 =====

    // 打开并读取 AFF 文件内容到预览区。
    private void BtnOpenAff_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Arcaea 谱面 (*.aff)|*.aff|所有文件 (*.*)|*.*",
            Title = "打开 .aff"
        };
        if (dlg.ShowDialog() != true) return;

        _vm.LoadedAffPath = dlg.FileName;
        _vm.AffPreview = File.ReadAllText(dlg.FileName, Encoding.UTF8);
        _vm.Status = $"已加载：{Path.GetFileName(dlg.FileName)}";
    }

    // 打开并解析 SPC 文件，同时刷新预览事件列表。
    private void BtnOpenSpc_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "In Falsus 谱面 (*.spc;*.txt)|*.spc;*.txt|所有文件 (*.*)|*.*",
            Title = "打开 .spc"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var spcText = File.ReadAllText(dlg.FileName, Encoding.UTF8);
            var events = SpcParser.Parse(spcText);

            _vm.GeneratedSpcText = spcText;
            _vm.SpcPreview = spcText;
            _vm.PreviewEvents = events;
            ResetPreviewEditHistory();

            // 计算事件的结束时间（用于预览时间范围）。
            static int EndTime(ISpcEvent ev) => ev switch
            {
                SpcHold h => h.TimeMs + h.DurationMs,
                SpcSkyArea s => s.TimeMs + s.DurationMs,
                _ => ev.TimeMs
            };

            _vm.PreviewMaxTimeMs = Math.Max(5000, events.Count > 0 ? events.Max(EndTime) : 5000);
            _vm.PreviewTimeMs = Math.Max(0, events
                .Where(x => x is not SpcChart)
                .Select(x => x.TimeMs)
                .DefaultIfEmpty(0).Min());

            _vm.Status = $"已加载 SPC：{Path.GetFileName(dlg.FileName)}（共 {events.Count} 个事件）";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "加载 SPC 失败");
            _vm.Status = "加载 SPC 失败。";
        }
    }

    // 读取 AFF、执行转换并刷新预览与输出文本。
    private void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.LoadedAffPath) || !File.Exists(_vm.LoadedAffPath))
        {
            MessageBox.Show("请先打开一个 .aff 文件。");
            return;
        }

        try
        {
            var affText = File.ReadAllText(_vm.LoadedAffPath, Encoding.UTF8);
            var aff = AffParser.Parse(affText);

            var opt = new ConverterOptions
            {
                MappingRule = _vm.MappingRule,
                Denominator = _vm.Denominator,
                SkyWidthRatio = _vm.SkyWidthRatio,
                XMapping = _vm.XMapping == "compress" ? "compress" : "clamp01",
                RecommendedKeymap = _vm.RecommendedKeymap,
                DisableLanes = _vm.DisableLanes,
                TapWidthPatternEnabled = _vm.TapWidthPatternEnabled,
                TapWidthPattern = _vm.TapWidthPattern,
                DenseTapThresholdMs = _vm.DenseTapThresholdMs,
                HoldWidthRandomEnabled = _vm.HoldWidthRandomEnabled,
                HoldWidthRandomMax = _vm.HoldWidthRandomMax,
                SkyareaStrategy2 = _vm.SkyareaStrategy2,
                RandomSeed = _vm.RandomSeed,
                // 自建规则参数映射
                NoteLaneMapping = _vm.NoteLaneMapping,
                NoteDefaultKind = _vm.NoteDefaultKind,
                HoldLaneMapping = _vm.HoldLaneMapping,
                HoldDefaultWidth = _vm.HoldDefaultWidth,
                HoldAllowNegativeDuration = _vm.HoldAllowNegativeDuration,
                ArcXMapping = _vm.ArcXMapping,
                ArcIgnoreY = _vm.ArcIgnoreY,
                FlickDirectionMode = _vm.FlickDirectionMode,
                FlickFixedDir = _vm.FlickFixedDir,
                FlickWidthMode = _vm.FlickWidthMode,
                FlickFixedWidthNum = _vm.FlickFixedWidthNum,
                FlickWidthRandomMax = _vm.FlickWidthRandomMax,
                // 高级设置
                GlobalTimeOffsetMs = _vm.GlobalTimeOffsetMs,
                MinHoldDurationMs = _vm.MinHoldDurationMs,
                MinSkyAreaDurationMs = _vm.MinSkyAreaDurationMs,
                OutputBpmChanges = _vm.OutputBpmChanges,
                DeduplicateTapThresholdMs = _vm.DeduplicateTapThresholdMs,
                SortMode = _vm.SortMode
            };

            var result = AffToSpcConverter.Convert.AffToSpcConverter.Convert(aff, opt);
            _vm.PreviewEvents = result.Events;
            ResetPreviewEditHistory();

            // 计算事件的结束时间（用于预览时间范围）。
            static int EndTime(ISpcEvent e) => e switch
            {
                SpcHold h => h.TimeMs + h.DurationMs,
                SpcSkyArea s => s.TimeMs + s.DurationMs,
                _ => e.TimeMs
            };

            _vm.PreviewMaxTimeMs = Math.Max(5000, result.Events.Max(EndTime));
            _vm.PreviewTimeMs = Math.Max(0, result.Events
                .Where(x => x is not SpcChart)
                .Select(x => x.TimeMs)
                .DefaultIfEmpty(0).Min());

            ValidationUtil.Validate(result.Events, opt, result.Warnings);

            var spcText = SpcWriter.Write(result.Events);
            _vm.GeneratedSpcText = spcText;
            _vm.SpcPreview = spcText;

            _vm.Status = $"转换成功：tap={result.Events.Count(x => x.Type == SpcEventType.Tap)}, " +
                         $"hold={result.Events.Count(x => x.Type == SpcEventType.Hold)}, " +
                         $"skyarea={result.Events.Count(x => x.Type == SpcEventType.SkyArea)}, " +
                         $"flick={result.Events.Count(x => x.Type == SpcEventType.Flick)}, " +
                         $"警告={result.Warnings.Count}";

            if (result.Warnings.Count > 0)
                MessageBox.Show(string.Join("\n", result.Warnings.Take(40)), "警告（前40条）");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "转换失败");
            _vm.Status = "转换失败。";
        }
    }

    // 将当前生成的 SPC 文本保存到文件。
    private void BtnSaveSpc_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.GeneratedSpcText))
        {
            MessageBox.Show("无内容可保存，请先进行转换。");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "In Falsus 谱面 (*.spc)|*.spc|所有文件 (*.*)|*.*",
            Title = "保存 .spc",
            FileName = "output.spc"
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, _vm.GeneratedSpcText, new UTF8Encoding(false));
        _vm.Status = $"已保存：{Path.GetFileName(dlg.FileName)}";
    }

    // 打开设置窗口。
    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this, DataContext = _vm };
        win.ShowDialog();
    }

    // 生成并显示当前 SPC 的简要报告。
    private void MenuReport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.GeneratedSpcText))
        {
            MessageBox.Show("请先完成转换，再生成报告。");
            return;
        }
        MessageBox.Show(ReportUtil.BuildSimpleReport(_vm.GeneratedSpcText, _vm), "报告");
    }

    // 打开资源打包窗口。
    private void MenuPackage_Click(object sender, RoutedEventArgs e)
    {
        new PackageWindow { Owner = this }.ShowDialog();
    }

    // 显示关于对话框。
    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Arcaea → In Falsus 转换器\nv0.1\nWPF/.NET", "关于");
    }

    // 关闭主窗口。
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    // 切换文本视图与可视化预览模式，并同步工具栏显示。
    private void OnPreviewToggled(object sender, RoutedEventArgs e)
    {
        bool show = MenuVisualPreview.IsChecked == true;

        if (!show && _vm.IsPlaying)
        {
            SyncPreviewTimeFromAudioStream();
            PausePlayback();
        }

        // 切换内容层
        PreviewOverlay.Visibility  = show ? Visibility.Visible   : Visibility.Collapsed;
        TextPanelsGrid.Visibility  = show ? Visibility.Collapsed : Visibility.Visible;

        // 切换工具栏：预览模式用背景音乐工具栏，普通模式用文件工具栏
        ToolbarText.Visibility    = show ? Visibility.Collapsed : Visibility.Visible;
        ToolbarPreview.Visibility = show ? Visibility.Visible   : Visibility.Collapsed;
    }

    // 从主界面输出区快速进入可视化预览。
    private void BtnOpenVisualPreview_Click(object sender, RoutedEventArgs e)
    {
        if (MenuVisualPreview.IsChecked == true) return;
        MenuVisualPreview.IsChecked = true;
        OnPreviewToggled(sender, e);
    }

    // ===== 音符增删 =====

    // 打开新增音符对话框，并进入预览放置模式。
    private void BtnAddNote_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.PreviewEvents == null)
        {
            MessageBox.Show("请先加载或转换谱面。");
            return;
        }

        int defaultDen = Math.Max(1, _vm.Denominator);
        var typeNames = new[] { "Tap", "Hold", "Flick", "SkyArea" };
        var dlg = new Views.AddNoteDialog(typeNames, defaultDen) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var request = dlg.SelectedType switch
        {
            "Tap" => new Views.AddNoteRequest
            {
                Type = Views.AddNoteType.Tap,
                GroundWidth = dlg.Kind
            },
            "Hold" => new Views.AddNoteRequest
            {
                Type = Views.AddNoteType.Hold,
                GroundWidth = dlg.Kind
            },
            "Flick" => new Views.AddNoteRequest
            {
                Type = Views.AddNoteType.Flick,
                Den = dlg.Den,
                WidthNum = dlg.WidthNum,
                Dir = dlg.Dir
            },
            "SkyArea" => new Views.AddNoteRequest
            {
                Type = Views.AddNoteType.SkyArea,
                Den = dlg.Den,
                WidthNum = dlg.WidthNum,
                WidthNum2 = dlg.WidthNum2,
                LeftEase = dlg.LeftEase,
                RightEase = dlg.RightEase,
                GroupId = dlg.GroupId
            },
            _ => null
        };

        if (request == null) return;

        _pendingAddRequest = request;
        PreviewControl.BeginAddNotePlacement(request);
        _vm.Status = "请在预览中点击/拖动以放置音符（已自动吸附到辅助线）";
    }

    // 接收预览放置结果并插入新音符到事件列表。
    private void OnNotePlacementCommitted(Views.AddNotePlacement placement)
    {
        if (_vm.PreviewEvents == null || _pendingAddRequest == null) return;
        var request = _pendingAddRequest;
        _pendingAddRequest = null;

        var events = _vm.PreviewEvents as List<ISpcEvent> ?? new List<ISpcEvent>(_vm.PreviewEvents);

        int timeMs = placement.StartTimeMs;
        int endTimeMs = placement.EndTimeMs;
        int duration = Math.Max(1, endTimeMs - timeMs);

        ISpcEvent? newEvent = request.Type switch
        {
            Views.AddNoteType.Tap => new SpcTap(timeMs, Math.Clamp(request.GroundWidth, 1, 4), placement.Lane),
            Views.AddNoteType.Hold => new SpcHold(timeMs, placement.Lane, Math.Clamp(request.GroundWidth, 1, 6), duration),
            Views.AddNoteType.Flick => new SpcFlick(timeMs,
                placement.PosNum,
                Math.Max(1, request.Den),
                Math.Max(1, request.WidthNum),
                request.Dir == 16 ? 16 : 4),
            Views.AddNoteType.SkyArea => new SpcSkyArea(timeMs,
                placement.PosNum, Math.Max(1, request.Den), Math.Max(0, request.WidthNum),
                placement.PosNum2, Math.Max(1, request.Den), Math.Max(0, request.WidthNum2),
                request.LeftEase, request.RightEase,
                duration, Math.Max(0, request.GroupId)),
            _ => null
        };

        if (newEvent == null) return;

        PushUndoSnapshotForPreviewEdit();
        events.Add(newEvent);
        events.Sort((a, b) =>
        {
            int c = a.TimeMs.CompareTo(b.TimeMs);
            if (c != 0) return c;
            return ((int)a.Type).CompareTo((int)b.Type);
        });

        ApplyEventsAndRefresh(events);
        _vm.Status = $"已添加 {newEvent.Type}（{newEvent.TimeMs}ms）";
    }

    // 更新事件列表并刷新文本与预览。
    private void ApplyEventsAndRefresh(List<ISpcEvent> events)
    {
        // 同步预览时间范围，避免新增/删除长音符后滑条上限滞后。
        static int EndTime(ISpcEvent e) => e switch
        {
            SpcHold h => h.TimeMs + h.DurationMs,
            SpcSkyArea s => s.TimeMs + s.DurationMs,
            _ => e.TimeMs
        };

        _vm.PreviewEvents = events;
        _vm.PreviewMaxTimeMs = Math.Max(5000, events.Count > 0 ? events.Max(EndTime) : 5000);
        _vm.PreviewTimeMs = Math.Clamp(_vm.PreviewTimeMs, 0, _vm.PreviewMaxTimeMs);
        var spcText = IO.SpcWriter.Write(events);
        _vm.GeneratedSpcText = spcText;
        _vm.SpcPreview = spcText;
        PreviewControl.RefreshModel();
        RefreshPreviewEditButtonStates();
    }

    // 清空预览编辑的撤回/恢复历史（通常在重新加载或重新转换谱面后调用）。
    private void ResetPreviewEditHistory()
    {
        _undoEventHistory.Clear();
        _redoEventHistory.Clear();
        RefreshPreviewEditButtonStates();
    }

    // 记录当前预览事件列表快照，供后续撤回。
    private void PushUndoSnapshotForPreviewEdit()
    {
        if (_vm.PreviewEvents == null) return;
        _undoEventHistory.Push(new List<ISpcEvent>(_vm.PreviewEvents));
        _redoEventHistory.Clear();
        RefreshPreviewEditButtonStates();
    }

    // 删除当前选中的音符并刷新预览与文本。
    private void BtnDeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.PreviewEvents == null || _selectedRenderItem?.SourceEvent == null)
        {
            MessageBox.Show("请先选中一个音符。");
            return;
        }

        var events = _vm.PreviewEvents as List<ISpcEvent>;
        if (events == null)
        {
            events = new List<ISpcEvent>(_vm.PreviewEvents);
        }

        int idx = _selectedRenderItem.SourceIndex;
        if (idx < 0 || idx >= events.Count)
        {
            MessageBox.Show("选中索引无效。");
            return;
        }

        var removed = events[idx];
        PushUndoSnapshotForPreviewEdit();
        events.RemoveAt(idx);

        // 更新预览与文本
        ApplyEventsAndRefresh(events);

        HidePropPanel();
        _vm.Status = $"已删除 {removed.Type}（{removed.TimeMs}ms）";
    }

    // 打开当前选中音符的编辑面板。
    private void BtnEditNote_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRenderItem?.SourceEvent == null)
        {
            MessageBox.Show("请先选中一个音符。");
            return;
        }

        ShowPropPanel(_selectedRenderItem);
    }

    // 撤回上一次预览编辑操作（添加/删除/编辑）。
    private void BtnUndoLastOperation_Click(object sender, RoutedEventArgs e)
    {
        if (_undoEventHistory.Count == 0)
        {
            _vm.Status = "没有可撤回的操作。";
            return;
        }

        if (_vm.PreviewEvents != null)
            _redoEventHistory.Push(new List<ISpcEvent>(_vm.PreviewEvents));

        var snapshot = _undoEventHistory.Pop();
        HidePropPanel();
        ApplyEventsAndRefresh(new List<ISpcEvent>(snapshot));
        _vm.Status = "已撤回上一次操作。";
    }

    // 恢复刚刚撤回的预览编辑操作。
    private void BtnRedoLastOperation_Click(object sender, RoutedEventArgs e)
    {
        if (_redoEventHistory.Count == 0)
        {
            _vm.Status = "没有可恢复的操作。";
            return;
        }

        if (_vm.PreviewEvents != null)
            _undoEventHistory.Push(new List<ISpcEvent>(_vm.PreviewEvents));

        var snapshot = _redoEventHistory.Pop();
        HidePropPanel();
        ApplyEventsAndRefresh(new List<ISpcEvent>(snapshot));
        _vm.Status = "已恢复上一次操作。";
    }

    // ===== 视图模式切换（合并/分离）=====

    // 切换到合并预览模式并更新按钮样式。
    private void OnMergedViewClick(object sender, RoutedEventArgs e)
    {
        PreviewControl.ViewMode = PreviewViewMode.Merged;
        BtnMergedView.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x66, 0xAA));
        BtnMergedView.Foreground = System.Windows.Media.Brushes.White;
        BtnSplitView.ClearValue(Button.BackgroundProperty);
        BtnSplitView.ClearValue(Button.ForegroundProperty);
    }

    // 切换到分离预览模式并更新按钮样式。
    private void OnSplitViewClick(object sender, RoutedEventArgs e)
    {
        PreviewControl.ViewMode = PreviewViewMode.Split;
        BtnSplitView.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x66, 0xAA));
        BtnSplitView.Foreground = System.Windows.Media.Brushes.White;
        BtnMergedView.ClearValue(Button.BackgroundProperty);
        BtnMergedView.ClearValue(Button.ForegroundProperty);
    }

    // ===== 音符选中与属性面板 =====

    // 收到预览控件的音符选中事件，更新当前选中项并按需关闭属性面板。
    private void OnNoteSelected(RenderItem? item)
    {
        if (_vm.IsPlaying && _vm.DisableNoteSelectionWhilePlaying)
        {
            HidePropPanel();
            return;
        }

        _selectedRenderItem = item;

        if (item == null || item.SourceEvent == null)
        {
            HidePropPanel();
            return;
        }

        RefreshPreviewEditButtonStates();
    }

    // 收到预览控件点击命中事件，根据设置决定是否打开音符编辑面板。
    private void OnPreviewNoteClicked(RenderItem? item, int clickCount)
    {
        if (_vm.IsPlaying && _vm.DisableNoteSelectionWhilePlaying)
        {
            HidePropPanel();
            return;
        }

        if (item == null || item.SourceEvent == null)
            return;

        bool editOnDoubleClick = _vm.PreviewNoteEditTriggerMode == "double";
        if (editOnDoubleClick)
        {
            if (clickCount >= 2)
                ShowPropPanel(item);
            else
                HidePropPanel(clearSelection: false);
            return;
        }

        ShowPropPanel(item);
    }

    // 展开属性面板并填充当前选中音符的字段。
    private void ShowPropPanel(RenderItem item)
    {
        PropPanel.Visibility = Visibility.Visible;
        // 加宽面板列，确保较长的字段名不被截断
        PropPanelColumn.Width = new GridLength(260);

        var src = item.SourceEvent!;
        PropHeader.Text = $"编辑音符：{src.Type}";
        PropTime.Text = src.TimeMs.ToString();

        // 清空上一次的动态字段
        PropFieldsPanel.Children.Clear();
        _propFields.Clear();
        _propComboFields.Clear();

        // 根据音符类型填充对应字段
        switch (src)
        {
            case SpcTap tap:
                BuildTapFields(tap);
                break;
            case SpcHold hold:
                BuildHoldFields(hold);
                break;
            case SpcFlick flick:
                BuildFlickFields(flick);
                break;
            case SpcSkyArea sky:
                BuildSkyAreaFields(sky);
                break;
        }
    }

    // ── 各音符类型的字段构建方法 ──────────────────────────────────────

    // 点按音符字段：宽度（1-4）、轨道（0-5）。
    private void BuildTapFields(SpcTap tap)
    {
        AddIntField("Kind",  tap.Kind.ToString(),      minVal: 1, maxVal: 4);
        AddIntField("Lane",  tap.LaneIndex.ToString(),  minVal: 0, maxVal: 5);
    }

    // 长按音符字段：轨道（0-5）、宽度（1-6）、时长（ms）（≥1）。
    private void BuildHoldFields(SpcHold hold)
    {
        AddIntField("Lane",          hold.LaneIndex.ToString(),  minVal: 0, maxVal: 5);
        AddIntField("Width",         hold.Width.ToString(),      minVal: 1, maxVal: 6);
        AddIntField("Duration (ms)", hold.DurationMs.ToString(), minVal: 1);
    }

    // 滑键音符字段：位置、分母（≥1）、宽度（≥1）、方向（4=右/16=左）。
    private void BuildFlickFields(SpcFlick flick)
    {
        AddIntField("PosNum",   flick.PosNum.ToString());
        AddIntField("Den",      flick.Den.ToString(),      minVal: 1);
        AddIntField("WidthNum", flick.WidthNum.ToString(), minVal: 1);
        // Dir 只有两个有效值：4（右）和 16（左），用下拉框
        AddComboField("Dir",
            new[] { "4（右）", "16（左）" },
            new[] { 4, 16 },
            flick.Dir == 16 ? 1 : 0);
    }

    // 天空区域字段：起点/终点位置与宽度、分母（锁定）、缓动、时长（ms）、组 ID。
    private void BuildSkyAreaFields(SpcSkyArea sky)
    {
        AddGroupHeader("Start");
        AddIntFieldWithKey("Start Position", "Position", sky.X1Num.ToString());
        AddIntFieldWithKey("Start Width", "Width", sky.W1Num.ToString(), minVal: 0);
        AddLockedIntFieldWithKey("Start Den", "Den", sky.Den1.ToString(), minVal: 1);

        AddGroupHeader("End");
        AddIntFieldWithKey("End Position", "Position", sky.X2Num.ToString());
        AddIntFieldWithKey("End Width", "Width", sky.W2Num.ToString(), minVal: 0);
        AddLockedIntFieldWithKey("End Den", "Den", sky.Den2.ToString(), minVal: 1);

        // 缓动：0=直线 / 1=Sine In / 2=Sine Out，使用下拉框约束
        AddComboField("L Ease",
            new[] { "0（直线）", "1（Sine In）", "2（Sine Out）" },
            new[] { 0, 1, 2 },
            Math.Clamp(sky.LeftEasing, 0, 2));
        AddComboField("R Ease",
            new[] { "0（直线）", "1（Sine In）", "2（Sine Out）" },
            new[] { 0, 1, 2 },
            Math.Clamp(sky.RightEasing, 0, 2));
        AddIntField("Duration (ms)", sky.DurationMs.ToString(), minVal: 1);
        AddIntField("GroupId",       sky.GroupId.ToString(),    minVal: 0);
    }

    // ── 属性面板通用控件工厂 ─────────────────────────────────────────

    // 添加带 −/+ 按钮的整数输入字段。
    // minVal 和 maxVal 为可选的合法值范围。
    private void AddIntField(string label, string value, int? minVal = null, int? maxVal = null)
    {
        AddIntFieldWithKey(label, label, value, minVal, maxVal);
    }

    // 添加相关内容或字段。
    private void AddIntFieldWithKey(string fieldKey, string label, string value, int? minVal = null, int? maxVal = null)
    {
        // 标签
        PropFieldsPanel.Children.Add(MakeLabelBlock(label));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var btnDec = new Button { Content = "−", FontSize = 14, Padding = new Thickness(0) };
        var tb = new TextBox
        {
            Text = value,
            TextAlignment = TextAlignment.Right,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(2, 0, 2, 0)
        };
        var btnInc = new Button { Content = "+", FontSize = 14, Padding = new Thickness(0) };

        // −/+ 按钮逻辑，带范围约束
        btnDec.Click += (_, _) =>
        {
            if (!int.TryParse(tb.Text, out int v)) return;
            int next = v - 1;
            if (minVal.HasValue) next = Math.Max(minVal.Value, next);
            tb.Text = next.ToString();
        };
        btnInc.Click += (_, _) =>
        {
            if (!int.TryParse(tb.Text, out int v)) return;
            int next = v + 1;
            if (maxVal.HasValue) next = Math.Min(maxVal.Value, next);
            tb.Text = next.ToString();
        };

        Grid.SetColumn(btnDec, 0);
        Grid.SetColumn(tb, 1);
        Grid.SetColumn(btnInc, 2);
        grid.Children.Add(btnDec);
        grid.Children.Add(tb);
        grid.Children.Add(btnInc);

        PropFieldsPanel.Children.Add(grid);
        _propFields.Add((fieldKey, tb));
    }

    // 添加相关内容或字段。
    private void AddLockedIntFieldWithKey(string fieldKey, string label, string value, int? minVal = null, int? maxVal = null)
    {
        var header = new Grid { Margin = new Thickness(0, 6, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA)),
            FontSize = 11
        };
        var toggle = new CheckBox
        {
            Content = "可编辑",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };

        Grid.SetColumn(text, 0);
        Grid.SetColumn(toggle, 1);
        header.Children.Add(text);
        header.Children.Add(toggle);
        PropFieldsPanel.Children.Add(header);

        var grid = new Grid { IsEnabled = false };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var btnDec = new Button { Content = "−", FontSize = 14, Padding = new Thickness(0) };
        var tb = new TextBox
        {
            Text = value,
            TextAlignment = TextAlignment.Right,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(2, 0, 2, 0)
        };
        var btnInc = new Button { Content = "+", FontSize = 14, Padding = new Thickness(0) };

        btnDec.Click += (_, _) =>
        {
            if (!int.TryParse(tb.Text, out int v)) return;
            int next = v - 1;
            if (minVal.HasValue) next = Math.Max(minVal.Value, next);
            tb.Text = next.ToString();
        };
        btnInc.Click += (_, _) =>
        {
            if (!int.TryParse(tb.Text, out int v)) return;
            int next = v + 1;
            if (maxVal.HasValue) next = Math.Min(maxVal.Value, next);
            tb.Text = next.ToString();
        };

        toggle.Checked += (_, _) => grid.IsEnabled = true;
        toggle.Unchecked += (_, _) => grid.IsEnabled = false;

        Grid.SetColumn(btnDec, 0);
        Grid.SetColumn(tb, 1);
        Grid.SetColumn(btnInc, 2);
        grid.Children.Add(btnDec);
        grid.Children.Add(tb);
        grid.Children.Add(btnInc);

        PropFieldsPanel.Children.Add(grid);
        _propFields.Add((fieldKey, tb));
    }

    // 添加相关内容或字段。
    private void AddGroupHeader(string text)
    {
        PropFieldsPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 2)
        });
    }

    // 添加相关内容或字段。
    private void AddComboField(string label, string[] displayItems, int[] values, int selectedIndex)
    {
        PropFieldsPanel.Children.Add(MakeLabelBlock(label));

        var cb = new ComboBox
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 0),
            Tag = values  // 将实际值数组保存在 Tag 中，应用时读取
        };
        foreach (var item in displayItems)
            cb.Items.Add(item);

        cb.SelectedIndex = Math.Clamp(selectedIndex, 0, displayItems.Length - 1);

        PropFieldsPanel.Children.Add(cb);
        _propComboFields.Add((label, cb));
    }

    // 构建属性面板标签样式的 TextBlock。
    private static TextBlock MakeLabelBlock(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA)),
        FontSize = 11,
        Margin = new Thickness(0, 6, 0, 2)
    };

    // ── 隐藏属性面板 ─────────────────────────────────────────────────

    // 隐藏属性面板，并可选地清除当前选中项。
    private void HidePropPanel(bool clearSelection = true)
    {
        PropPanel.Visibility = Visibility.Collapsed;
        PropPanelColumn.Width = new GridLength(0);
        if (clearSelection)
        {
            _selectedRenderItem = null;
            PreviewControl.SelectedItemIndex = -1;
        }
        RefreshPreviewEditButtonStates();
    }

    // ── 时间字段微调 ─────────────────────────────────────────────────

    // 将属性面板中的时间字段减小一个步长。
    private void PropDecTime(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PropTime.Text, out int v)) PropTime.Text = Math.Max(0, v - 1).ToString();
    }

    // 将属性面板中的时间字段增大一个步长。
    private void PropIncTime(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PropTime.Text, out int v)) PropTime.Text = (v + 1).ToString();
    }

    // 放弃本次属性编辑并关闭属性面板。
    private void PropCancel_Click(object sender, RoutedEventArgs e) => HidePropPanel();

    // 应用属性面板修改并回写到事件列表。
    private void PropApply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRenderItem?.SourceEvent == null || _vm.PreviewEvents == null)
            return;

        var events = _vm.PreviewEvents as List<ISpcEvent>;
        if (events == null) return;

        int idx = _selectedRenderItem.SourceIndex;
        if (idx < 0 || idx >= events.Count) return;

        if (!int.TryParse(PropTime.Text, out int timeMs))
        {
            MessageBox.Show("时间值无效。");
            return;
        }

        // 将整数字段读入字典
        var fields = new Dictionary<string, string>();
        foreach (var (name, tb) in _propFields)
            fields[name] = tb.Text;

        // 将下拉框字段读入字典（取实际整数值）
        var comboValues = new Dictionary<string, int>();
        foreach (var (name, cb) in _propComboFields)
        {
            if (cb.Tag is int[] vals && cb.SelectedIndex >= 0 && cb.SelectedIndex < vals.Length)
                comboValues[name] = vals[cb.SelectedIndex];
        }

        ISpcEvent? newEvent = null;
        try
        {
            newEvent = _selectedRenderItem.SourceEvent switch
            {
                SpcTap => BuildTapEvent(timeMs, fields),
                SpcHold => BuildHoldEvent(timeMs, fields),
                SpcFlick => BuildFlickEvent(timeMs, fields, comboValues),
                SpcSkyArea => BuildSkyAreaEvent(timeMs, fields, comboValues),
                _ => null
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show($"字段值无效：{ex.Message}");
            return;
        }

        if (newEvent == null) return;

        PushUndoSnapshotForPreviewEdit();
        // 写回事件列表
        events[idx] = newEvent;

        events.Sort((a, b) =>
        {
            int c = a.TimeMs.CompareTo(b.TimeMs);
            if (c != 0) return c;
            return ((int)a.Type).CompareTo((int)b.Type);
        });

        // 更新 SPC 文本与预览
        ApplyEventsAndRefresh(events);

        _vm.Status = $"已应用 {newEvent.Type} 音符修改（{timeMs}ms）";
        HidePropPanel();
    }

    // ── 应用时各类型事件构建辅助方法 ──────────────────────────────

    // 根据属性字段构建点按音符事件。
    private static SpcTap BuildTapEvent(int timeMs, Dictionary<string, string> f)
        => new(timeMs,
               Math.Clamp(ParseInt(f, "Kind"), 1, 4),
               Math.Clamp(ParseInt(f, "Lane"), 0, 5));

    // 根据属性字段构建长按音符事件。
    private static SpcHold BuildHoldEvent(int timeMs, Dictionary<string, string> f)
        => new(timeMs,
               Math.Clamp(ParseInt(f, "Lane"),          0, 5),
               Math.Clamp(ParseInt(f, "Width"),         1, 6),
               Math.Max(1, ParseInt(f, "Duration (ms)")));

    // 根据属性字段构建滑键音符事件。
    private static SpcFlick BuildFlickEvent(int timeMs, Dictionary<string, string> f,
                                             Dictionary<string, int> combo)
    {
        int dir = combo.TryGetValue("Dir", out int d) ? d : ParseInt(f, "Dir");
        return new(timeMs,
                   ParseInt(f, "PosNum"),
                   Math.Max(1, ParseInt(f, "Den")),
                   Math.Max(1, ParseInt(f, "WidthNum")),
                   dir);
    }

    // 根据属性字段构建天空区域事件。
    private static SpcSkyArea BuildSkyAreaEvent(int timeMs, Dictionary<string, string> f,
                                                 Dictionary<string, int> combo)
    {
        int startDen = Math.Max(1, ParseInt(f, "Start Den"));
        int endDen = Math.Max(1, ParseInt(f, "End Den"));
        int lEase = combo.TryGetValue("L Ease", out int le) ? le : Math.Clamp(ParseInt(f, "L Ease"), 0, 2);
        int rEase = combo.TryGetValue("R Ease", out int re) ? re : Math.Clamp(ParseInt(f, "R Ease"), 0, 2);
        return new(timeMs,
                   ParseInt(f, "Start Position"), startDen, Math.Max(0, ParseInt(f, "Start Width")),
                   ParseInt(f, "End Position"), endDen, Math.Max(0, ParseInt(f, "End Width")),
                   lEase, rEase,
                   Math.Max(1, ParseInt(f, "Duration (ms)")),
                   Math.Max(0, ParseInt(f, "GroupId")));
    }

    // 解析相关数据并返回结果。
    private static int ParseInt(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var val))
            throw new ArgumentException($"缺少字段：{key}");
        if (!int.TryParse(val, out int result))
            throw new FormatException($"{key} 值无效：{val}");
        return result;
    }

    // ===== 背景音乐控制 =====

    // 选择并加载背景音乐文件。
    private void BtnOpenBgm_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "音频文件 (*.mp3;*.wav;*.ogg;*.flac)|*.mp3;*.wav;*.ogg;*.flac|所有文件 (*.*)|*.*",
            Title = "打开 BGM"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            StopPlayback();
            DisposeAudio();

            string path = dlg.FileName;
            string ext = Path.GetExtension(path).ToLowerInvariant();

            // 根据扩展名选择解码器
            WaveStream raw = ext switch
            {
                ".ogg" => new VorbisWaveReader(path),
                ".mp3" => new Mp3FileReader(path),
                ".wav" => new WaveFileReader(path),
                _ => new AudioFileReader(path)
            };

            _audioStream = raw;
            var waveOut = new WaveOutEvent
            {
                DesiredLatency = 40,
                NumberOfBuffers = 2
            };
            waveOut.Init(raw);
            _waveOut = waveOut;
            _waveOut.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);

            _vm.BgmPath = path;
            _vm.Status = $"BGM：{Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _vm.Status = $"BGM 加载失败：{ex.Message}";
        }
    }

    // 切换背景音乐播放/暂停状态。
    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsPlaying) PausePlayback();
        else StartPlayback();
    }

    // 重置播放位置并按需重新开始播放。
    private void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        bool wasPlaying = _vm.IsPlaying;
        StopPlayback();

        _vm.PreviewTimeMs = 0;
        PreviewControl.JudgeTimeMs = 0;

        if (wasPlaying || _waveOut != null)
            StartPlayback();
    }

    // 开始背景音乐播放并同步预览。
    private void StartPlayback()
    {
        if (_waveOut == null || _audioStream == null) return;

        double seekSec = Math.Max(0, _vm.PreviewTimeMs / 1000.0);
        if (_audioStream.CanSeek)
            _audioStream.CurrentTime = TimeSpan.FromSeconds(seekSec);

        ResetPreviewAudioClock();
        _waveOut.Play();
        _vm.IsPlaying = true;
        HookRenderCallback(true);
    }

    // 暂停背景音乐播放。
    private void PausePlayback()
    {
        _waveOut?.Pause();
        ResetPreviewAudioClock();
        _vm.IsPlaying = false;
        HookRenderCallback(false);
    }

    // 停止背景音乐播放并重置状态。
    private void StopPlayback()
    {
        _waveOut?.Stop();
        ResetPreviewAudioClock();
        _vm.IsPlaying = false;
        HookRenderCallback(false);
    }

    // 从音频流读取当前位置并同步到预览时间，供退出预览或暂停前记录位置。
    private void SyncPreviewTimeFromAudioStream()
    {
        if (_audioStream == null) return;

        double audioMs = _audioStream.CurrentTime.TotalMilliseconds;
        if (double.IsNaN(audioMs) || double.IsInfinity(audioMs) || audioMs < 0) return;

        int ms = Math.Max(0, (int)Math.Round(audioMs));
        _vm.PreviewTimeMs = ms;
        PreviewControl.JudgeTimeMs = ms;
        PreviewControl.JudgeTimeMsPrecise = audioMs;
    }

    private int _syncFrameCounter = 0;
    private bool _previewAudioClockReady;
    private long _previewAudioClockRefTick;
    private double _previewAudioClockRefMs;

    // 重置预览用的音频平滑时钟，下次帧回调会重新对齐真实播放时间。
    private void ResetPreviewAudioClock()
    {
        _previewAudioClockReady = false;
        _previewAudioClockRefTick = 0;
        _previewAudioClockRefMs = 0;
    }

    // 使用“真实音频时间 + 本地时钟外推 + 小幅纠偏”生成更平滑的预览时间。
    private double GetSmoothedAudioMs(double actualAudioMs)
    {
        long nowTick = Stopwatch.GetTimestamp();
        if (!_previewAudioClockReady)
        {
            _previewAudioClockReady = true;
            _previewAudioClockRefTick = nowTick;
            _previewAudioClockRefMs = actualAudioMs;
            return actualAudioMs;
        }

        double elapsedMs = (nowTick - _previewAudioClockRefTick) * 1000.0 / Stopwatch.Frequency;
        double predictedMs = _previewAudioClockRefMs + elapsedMs;
        double errorMs = actualAudioMs - predictedMs;

        // seek、暂停恢复或底层时钟突跳时直接重同步，避免缓慢追赶。
        if (Math.Abs(errorMs) > 40.0)
        {
            _previewAudioClockRefTick = nowTick;
            _previewAudioClockRefMs = actualAudioMs;
            return actualAudioMs;
        }

        // 正常播放时缓慢纠偏，减轻 CurrentTime 按音频块跳变导致的视觉抖动。
        _previewAudioClockRefMs += errorMs * 0.20;
        return _previewAudioClockRefMs + (nowTick - _previewAudioClockRefTick) * 1000.0 / Stopwatch.Frequency;
    }

    // 每帧渲染回调：将音频播放位置同步到预览时间轴。
    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!_vm.IsPlaying || _audioStream == null) return;
        double actualAudioMs = _audioStream.CurrentTime.TotalMilliseconds;
        double audioMs = GetSmoothedAudioMs(actualAudioMs);
        if (audioMs < 0) return;

        int ms = (int)Math.Round(audioMs);
        if (++_syncFrameCounter >= 6)
        {
            _syncFrameCounter = 0;
            _vm.PreviewTimeMs = ms;
            PreviewControl.JudgeTimeMs = ms;
        }

        // 始终最后写入精确时间，避免整数毫秒同步覆盖渲染快照造成周期性抖动。
        PreviewControl.JudgeTimeMsPrecise = audioMs;
    }

    // 绑定或解绑预览渲染帧回调。
    private void HookRenderCallback(bool hook)
    {
        if (hook && !_renderHooked)
        {
            CompositionTarget.Rendering += OnRenderingFrame;
            _renderHooked = true;
        }
        else if (!hook && _renderHooked)
        {
            CompositionTarget.Rendering -= OnRenderingFrame;
            _renderHooked = false;
        }
    }

    // 释放背景音乐播放资源。
    private void DisposeAudio()
    {
        _waveOut?.Dispose();
        _waveOut = null;
        _audioStream?.Dispose();
        _audioStream = null;
    }

    // 窗口关闭时停止播放并释放音频资源。
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        StopPlayback();
        DisposeAudio();
    }

    // 退出可视化预览模式并切回文本视图。
    private void BtnExitPreview_Click(object sender, RoutedEventArgs e)
    {
        MenuVisualPreview.IsChecked = false;
        OnPreviewToggled(sender, e);
    }
}

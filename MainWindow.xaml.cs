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

    // 通用数字字段：(字段名, TextBox)
    private readonly List<(string fieldName, TextBox textBox)> _propFields = new();
    // 下拉框字段：(字段名, ComboBox) — 用于枚举/有限选项
    private readonly List<(string fieldName, ComboBox comboBox)> _propComboFields = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        // 绑定音符选中事件
        PreviewControl.NoteSelected += OnNoteSelected;
    }

    // ===== 文件操作 =====

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

            // 计算预览时间范围
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
                RandomSeed = _vm.RandomSeed
            };

            var result = AffToSpcConverter.Convert.AffToSpcConverter.Convert(aff, opt);
            _vm.PreviewEvents = result.Events;

            // 计算预览时间范围
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

    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this, DataContext = _vm };
        win.ShowDialog();
    }

    private void MenuReport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.GeneratedSpcText))
        {
            MessageBox.Show("请先完成转换，再生成报告。");
            return;
        }
        MessageBox.Show(ReportUtil.BuildSimpleReport(_vm.GeneratedSpcText, _vm), "报告");
    }

    private void MenuPackage_Click(object sender, RoutedEventArgs e)
    {
        new PackageWindow { Owner = this }.ShowDialog();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Arcaea → In Falsus 转换器\nv0.1\nWPF/.NET", "关于");
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 菜单栏"可视化预览"选中/取消时切换工具栏与内容区可见性。
    /// </summary>
    private void OnPreviewToggled(object sender, RoutedEventArgs e)
    {
        bool show = MenuVisualPreview.IsChecked == true;

        // 切换内容层
        PreviewOverlay.Visibility  = show ? Visibility.Visible   : Visibility.Collapsed;
        TextPanelsGrid.Visibility  = show ? Visibility.Collapsed : Visibility.Visible;

        // 切换工具栏：预览模式用背景音乐工具栏，普通模式用文件工具栏
        ToolbarText.Visibility    = show ? Visibility.Collapsed : Visibility.Visible;
        ToolbarPreview.Visibility = show ? Visibility.Visible   : Visibility.Collapsed;
    }

    // ===== 视图模式切换（合并/分离）=====

    private void OnMergedViewClick(object sender, RoutedEventArgs e)
    {
        PreviewControl.ViewMode = PreviewViewMode.Merged;
        BtnMergedView.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x66, 0xAA));
        BtnMergedView.Foreground = System.Windows.Media.Brushes.White;
        BtnSplitView.ClearValue(Button.BackgroundProperty);
        BtnSplitView.ClearValue(Button.ForegroundProperty);
    }

    private void OnSplitViewClick(object sender, RoutedEventArgs e)
    {
        PreviewControl.ViewMode = PreviewViewMode.Split;
        BtnSplitView.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x66, 0xAA));
        BtnSplitView.Foreground = System.Windows.Media.Brushes.White;
        BtnMergedView.ClearValue(Button.BackgroundProperty);
        BtnMergedView.ClearValue(Button.ForegroundProperty);
    }

    // ===== 音符选中与属性面板 =====

    /// <summary>收到预览控件的音符选中事件，打开或关闭属性面板。</summary>
    private void OnNoteSelected(RenderItem? item)
    {
        _selectedRenderItem = item;

        if (item == null || item.SourceEvent == null)
        {
            HidePropPanel();
            return;
        }

        ShowPropPanel(item);
    }

    /// <summary>展开属性面板并填充当前选中音符的字段。</summary>
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

    /// <summary>点按音符字段：宽度（1-4）、轨道（0-5）。</summary>
    private void BuildTapFields(SpcTap tap)
    {
        AddIntField("Kind",  tap.Kind.ToString(),      minVal: 1, maxVal: 4);
        AddIntField("Lane",  tap.LaneIndex.ToString(),  minVal: 0, maxVal: 5);
    }

    /// <summary>长按音符字段：轨道（0-5）、宽度（1-6）、时长（ms）（≥1）。</summary>
    private void BuildHoldFields(SpcHold hold)
    {
        AddIntField("Lane",          hold.LaneIndex.ToString(),  minVal: 0, maxVal: 5);
        AddIntField("Width",         hold.Width.ToString(),      minVal: 1, maxVal: 6);
        AddIntField("Duration (ms)", hold.DurationMs.ToString(), minVal: 1);
    }

    /// <summary>滑键音符字段：位置、分母（≥1）、宽度（≥1）、方向（4=右/16=左）。</summary>
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

    /// <summary>天空区域字段：起点/终点位置与宽度、分母（锁定）、缓动、时长（ms）、组 ID。</summary>
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

    /// <summary>
    /// 添加带 −/+ 按钮的整数输入字段。
    /// <paramref name="minVal"/> 和 <paramref name="maxVal"/> 为可选的合法值范围。
    /// </summary>
    private void AddIntField(string label, string value, int? minVal = null, int? maxVal = null)
    {
        AddIntFieldWithKey(label, label, value, minVal, maxVal);
    }

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

    /// <summary>
    /// 添加下拉框字段，用于只有有限合法值的字段（如缓动、方向）。
    /// </summary>
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

    /// <summary>构建属性面板标签样式的 TextBlock。</summary>
    private static TextBlock MakeLabelBlock(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA)),
        FontSize = 11,
        Margin = new Thickness(0, 6, 0, 2)
    };

    // ── 隐藏属性面板 ─────────────────────────────────────────────────

    private void HidePropPanel()
    {
        PropPanel.Visibility = Visibility.Collapsed;
        PropPanelColumn.Width = new GridLength(0);
        _selectedRenderItem = null;
        PreviewControl.SelectedItemIndex = -1;
    }

    // ── 时间字段微调 ─────────────────────────────────────────────────

    private void PropDecTime(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PropTime.Text, out int v)) PropTime.Text = Math.Max(0, v - 1).ToString();
    }

    private void PropIncTime(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PropTime.Text, out int v)) PropTime.Text = (v + 1).ToString();
    }

    private void PropCancel_Click(object sender, RoutedEventArgs e) => HidePropPanel();

    /// <summary>
    /// 将属性面板中的修改应用到事件列表，并刷新预览与 SPC 文本。
    /// </summary>
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

        // 写回事件列表
        events[idx] = newEvent;

        // 更新 SPC 文本与预览
        var spcText = SpcWriter.Write(events);
        _vm.GeneratedSpcText = spcText;
        _vm.SpcPreview = spcText;
        _vm.PreviewEvents = events;
        PreviewControl.RefreshModel();

        _vm.Status = $"已应用 {newEvent.Type} 音符修改（{timeMs}ms）";
        HidePropPanel();
    }

    // ── 应用时各类型事件构建辅助方法 ──────────────────────────────

    private static SpcTap BuildTapEvent(int timeMs, Dictionary<string, string> f)
        => new(timeMs,
               Math.Clamp(ParseInt(f, "Kind"), 1, 4),
               Math.Clamp(ParseInt(f, "Lane"), 0, 5));

    private static SpcHold BuildHoldEvent(int timeMs, Dictionary<string, string> f)
        => new(timeMs,
               Math.Clamp(ParseInt(f, "Lane"),          0, 5),
               Math.Clamp(ParseInt(f, "Width"),         1, 6),
               Math.Max(1, ParseInt(f, "Duration (ms)")));

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

    private static int ParseInt(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var val))
            throw new ArgumentException($"缺少字段：{key}");
        if (!int.TryParse(val, out int result))
            throw new FormatException($"{key} 值无效：{val}");
        return result;
    }

    // ===== 背景音乐控制 =====

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
            _waveOut = new WaveOutEvent();
            _waveOut.Init(raw);
            _waveOut.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);

            _vm.BgmPath = path;
            _vm.Status = $"BGM：{Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _vm.Status = $"BGM 加载失败：{ex.Message}";
        }
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsPlaying) PausePlayback();
        else StartPlayback();
    }

    private void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        bool wasPlaying = _vm.IsPlaying;
        StopPlayback();

        _vm.PreviewTimeMs = 0;
        PreviewControl.JudgeTimeMs = 0;

        if (wasPlaying || _waveOut != null)
            StartPlayback();
    }

    private void StartPlayback()
    {
        if (_waveOut == null || _audioStream == null) return;

        double seekSec = Math.Max(0, _vm.PreviewTimeMs / 1000.0);
        if (_audioStream.CanSeek)
            _audioStream.CurrentTime = TimeSpan.FromSeconds(seekSec);

        _waveOut.Play();
        _vm.IsPlaying = true;
        HookRenderCallback(true);
    }

    private void PausePlayback()
    {
        _waveOut?.Pause();
        _vm.IsPlaying = false;
        HookRenderCallback(false);
    }

    private void StopPlayback()
    {
        _waveOut?.Stop();
        _vm.IsPlaying = false;
        HookRenderCallback(false);
    }

    private int _syncFrameCounter = 0;

    /// <summary>每帧渲染回调：将音频播放位置同步到预览时间轴。</summary>
    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!_vm.IsPlaying || _audioStream == null) return;
        double audioMs = _audioStream.CurrentTime.TotalMilliseconds;
        if (audioMs < 0) return;

        int ms = (int)Math.Round(audioMs);
        PreviewControl.JudgeTimeMsPrecise = audioMs;
        
        if (++_syncFrameCounter >= 6)
        {
            _syncFrameCounter = 0;
            _vm.PreviewTimeMs = ms;
            PreviewControl.JudgeTimeMs = ms;
        }
    }

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

    private void DisposeAudio()
    {
        _waveOut?.Dispose();
        _waveOut = null;
        _audioStream?.Dispose();
        _audioStream = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        StopPlayback();
        DisposeAudio();
    }
}
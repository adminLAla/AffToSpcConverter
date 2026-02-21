using AffToSpcConverter.Convert;
using AffToSpcConverter.IO;
using AffToSpcConverter.Parsing;
using AffToSpcConverter.Utils;
using AffToSpcConverter.ViewModels;
using AffToSpcConverter.Views;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace AffToSpcConverter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void BtnOpenAff_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Arcaea chart (*.aff)|*.aff|All files (*.*)|*.*",
            Title = "Open .aff"
        };
        if (dlg.ShowDialog() != true) return;

        _vm.LoadedAffPath = dlg.FileName;
        _vm.AffPreview = File.ReadAllText(dlg.FileName, Encoding.UTF8);
        _vm.Status = $"Loaded: {Path.GetFileName(dlg.FileName)}";
    }

    private void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.LoadedAffPath) || !File.Exists(_vm.LoadedAffPath))
        {
            MessageBox.Show("Please open an .aff first.");
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

            // 额外做一次校验（可选，但强烈建议保留）
            ValidationUtil.Validate(result.Events, opt, result.Warnings);

            var spcText = SpcWriter.Write(result.Events);

            _vm.GeneratedSpcText = spcText;
            _vm.SpcPreview = string.Join('\n', spcText.Split('\n').Take(500));

            _vm.Status = $"OK: tap={result.Events.Count(x => x.Type == Models.SpcEventType.Tap)}, " +
                         $"hold={result.Events.Count(x => x.Type == Models.SpcEventType.Hold)}, " +
                         $"skyarea={result.Events.Count(x => x.Type == Models.SpcEventType.SkyArea)}, " +
                         $"flick={result.Events.Count(x => x.Type == Models.SpcEventType.Flick)}, " +
                         $"warn={result.Warnings.Count}";

            if (result.Warnings.Count > 0)
            {
                MessageBox.Show(string.Join("\n", result.Warnings.Take(40)), "Warnings (top 40)");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Convert failed");
            _vm.Status = "Convert failed.";
        }
    }

    private void BtnSaveSpc_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.GeneratedSpcText))
        {
            MessageBox.Show("Nothing to save. Convert first.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "In Falsus chart (*.spc)|*.spc|All files (*.*)|*.*",
            Title = "Save .spc",
            FileName = "output.spc"
        };
        if (dlg.ShowDialog() != true) return;

        // 默认不写 BOM
        File.WriteAllText(dlg.FileName, _vm.GeneratedSpcText, new UTF8Encoding(false));
        _vm.Status = $"Saved: {Path.GetFileName(dlg.FileName)}";
    }

    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow
        {
            Owner = this,
            DataContext = _vm
        };
        win.ShowDialog();
    }

    private void MenuReport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.GeneratedSpcText))
        {
            MessageBox.Show("Convert first, then generate report.");
            return;
        }

        // 简易报告：统计 + 当前关键参数
        var report = ReportUtil.BuildSimpleReport(_vm.GeneratedSpcText, _vm);
        MessageBox.Show(report, "Report");
    }

    private void MenuPackage_Click(object sender, RoutedEventArgs e)
    {
        var win = new PackageWindow
        {
            Owner = this
        };
        win.ShowDialog();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Arcaea → In Falsus Converter\nv0.1\nWPF/.NET", "About");
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
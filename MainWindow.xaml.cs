using AffToSpcConverter.Convert;
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
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace AffToSpcConverter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    // ===== Audio (NAudio) =====
    private IWavePlayer? _waveOut;
    private WaveStream? _audioStream;
    // 用 CompositionTarget.Rendering 替代 DispatcherTimer，与 WPF 渲染帧同步
    private bool _renderHooked = false;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    // ===== File handlers =====

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

    private void BtnOpenSpc_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "In Falsus chart (*.spc)|*.spc|All files (*.*)|*.*",
            Title = "Open .spc"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var spcText = File.ReadAllText(dlg.FileName, Encoding.UTF8);
            var events = SpcParser.Parse(spcText);

            _vm.GeneratedSpcText = spcText;
            _vm.SpcPreview = spcText;
            _vm.PreviewEvents = events;

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

            _vm.Status = $"Loaded SPC: {Path.GetFileName(dlg.FileName)} ({events.Count} events)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Load SPC failed");
            _vm.Status = "Load SPC failed.";
        }
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
            _vm.PreviewEvents = result.Events;

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

            _vm.Status = $"OK: tap={result.Events.Count(x => x.Type == SpcEventType.Tap)}, " +
                         $"hold={result.Events.Count(x => x.Type == SpcEventType.Hold)}, " +
                         $"skyarea={result.Events.Count(x => x.Type == SpcEventType.SkyArea)}, " +
                         $"flick={result.Events.Count(x => x.Type == SpcEventType.Flick)}, " +
                         $"warn={result.Warnings.Count}";

            if (result.Warnings.Count > 0)
                MessageBox.Show(string.Join("\n", result.Warnings.Take(40)), "Warnings (top 40)");
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

        File.WriteAllText(dlg.FileName, _vm.GeneratedSpcText, new UTF8Encoding(false));
        _vm.Status = $"Saved: {Path.GetFileName(dlg.FileName)}";
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
            MessageBox.Show("Convert first, then generate report.");
            return;
        }
        MessageBox.Show(ReportUtil.BuildSimpleReport(_vm.GeneratedSpcText, _vm), "Report");
    }

    private void MenuPackage_Click(object sender, RoutedEventArgs e)
    {
        new PackageWindow { Owner = this }.ShowDialog();
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Arcaea → In Falsus Converter\nv0.1\nWPF/.NET", "About");
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewToggled(object sender, RoutedEventArgs e)
    {
        bool show = BtnShowPreview.IsChecked == true;
        PreviewOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TextPanelsGrid.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    // ===== Audio handlers (NAudio) =====

    private void BtnOpenBgm_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Audio files (*.mp3;*.wav;*.ogg;*.flac)|*.mp3;*.wav;*.ogg;*.flac|All files (*.*)|*.*",
            Title = "Open BGM"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            StopPlayback();
            DisposeAudio();

            string path = dlg.FileName;
            string ext = Path.GetExtension(path).ToLowerInvariant();

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
            _vm.Status = $"BGM: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _vm.Status = $"BGM load failed: {ex.Message}";
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

    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!_vm.IsPlaying || _audioStream == null) return;
        double audioMs = _audioStream.CurrentTime.TotalMilliseconds;
        if (audioMs < 0) return;

        int ms = (int)Math.Round(audioMs);
        PreviewControl.JudgeTimeMsPrecise = audioMs;
        PreviewControl.JudgeTimeMs = ms;

        if (++_syncFrameCounter >= 6)
        {
            _syncFrameCounter = 0;
            _vm.PreviewTimeMs = ms;
        }
    }

    private int _syncFrameCounter = 0;

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
using AffToSpcConverter.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AffToSpcConverter.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _status = "Ready.";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    private int _denominator = 24;
    public int Denominator { get => _denominator; set { _denominator = value; OnPropertyChanged(); } }

    private double _skyWidthRatio = 0.25;
    public double SkyWidthRatio { get => _skyWidthRatio; set { _skyWidthRatio = value; OnPropertyChanged(); } }

    private string _xMapping = "clamp01";
    public string XMapping { get => _xMapping; set { _xMapping = value; OnPropertyChanged(); } }

    private bool _recommendedKeymap;
    public bool RecommendedKeymap { get => _recommendedKeymap; set { _recommendedKeymap = value; OnPropertyChanged(); } }

    private bool _disableLanes;
    public bool DisableLanes { get => _disableLanes; set { _disableLanes = value; OnPropertyChanged(); } }

    // ---- 可选功能 ----

    private bool _tapWidthPatternEnabled;
    public bool TapWidthPatternEnabled { get => _tapWidthPatternEnabled; set { _tapWidthPatternEnabled = value; OnPropertyChanged(); } }

    private string _tapWidthPattern = "1,2";
    public string TapWidthPattern { get => _tapWidthPattern; set { _tapWidthPattern = value; OnPropertyChanged(); } }

    private int _denseTapThresholdMs = 0; // 0 表示自动（以基准 BPM 的 16 分音符）
    public int DenseTapThresholdMs { get => _denseTapThresholdMs; set { _denseTapThresholdMs = value; OnPropertyChanged(); } }

    private bool _holdWidthRandomEnabled;
    public bool HoldWidthRandomEnabled { get => _holdWidthRandomEnabled; set { _holdWidthRandomEnabled = value; OnPropertyChanged(); } }

    private int _holdWidthRandomMax = 2;
    public int HoldWidthRandomMax { get => _holdWidthRandomMax; set { _holdWidthRandomMax = value; OnPropertyChanged(); } }

    private bool _skyareaStrategy2;
    public bool SkyareaStrategy2 { get => _skyareaStrategy2; set { _skyareaStrategy2 = value; OnPropertyChanged(); } }

    private int _randomSeed = 12345;
    public int RandomSeed { get => _randomSeed; set { _randomSeed = value; OnPropertyChanged(); } }

    // ---- 预览 ----
    private string _affPreview = "";
    public string AffPreview { get => _affPreview; set { _affPreview = value; OnPropertyChanged(); } }

    private string _spcPreview = "";
    public string SpcPreview { get => _spcPreview; set { _spcPreview = value; OnPropertyChanged(); } }

    public string? LoadedAffPath { get; set; }
    public string? GeneratedSpcText { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ---- 可视化预览 ----
    private IReadOnlyList<ISpcEvent>? _previewEvents;
    public IReadOnlyList<ISpcEvent>? PreviewEvents
    {
        get => _previewEvents;
        set { _previewEvents = value; OnPropertyChanged(); }
    }

    private int _previewTimeMs;
    public int PreviewTimeMs
    {
        get => _previewTimeMs;
        set
        {
            if (_previewTimeMs == value) return;
            _previewTimeMs = value;
            OnPropertyChanged();
        }
    }

    private int _previewMaxTimeMs = 10000;
    public int PreviewMaxTimeMs
    {
        get => _previewMaxTimeMs;
        set
        {
            if (_previewMaxTimeMs == value) return;
            _previewMaxTimeMs = value;
            OnPropertyChanged();
        }
    }

    // 类似视频里的速度：1.20 h/s（这里用 PixelsPerSecond 表示“1秒=多少像素”）
    private double _previewPixelsPerSecond = 1000; // 默认更接近游戏观感
    public double PreviewPixelsPerSecond
    {
        get => _previewPixelsPerSecond;
        set
        {
            if (System.Math.Abs(_previewPixelsPerSecond - value) < 0.0001) return;
            _previewPixelsPerSecond = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewPixelsPerSecondEffective));
        }
    }

    private double _previewSpeed = 1.0;
    public double PreviewSpeed
    {
        get => _previewSpeed;
        set
        {
            var next = value <= 0 ? 0.01 : value;
            if (System.Math.Abs(_previewSpeed - next) < 0.0001) return;
            _previewSpeed = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewPixelsPerSecondEffective));
        }
    }

    public double PreviewPixelsPerSecondEffective => PreviewPixelsPerSecond * PreviewSpeed;

    // ---- 音频 ----
    private string? _bgmPath;
    public string? BgmPath
    {
        get => _bgmPath;
        set { _bgmPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(BgmFileName)); }
    }

    public string BgmFileName => System.IO.Path.GetFileName(_bgmPath ?? "") is { Length: > 0 } s ? s : "No BGM";

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); }
    }

    private int _previewTargetFps = 144;
    public int PreviewTargetFps
    {
        get => _previewTargetFps;
        set
        {
            if (_previewTargetFps == value) return;
            _previewTargetFps = value;
            OnPropertyChanged();
        }
    }

    private bool _showFpsStats = true;
    public bool ShowFpsStats
    {
        get => _showFpsStats;
        set
        {
            if (_showFpsStats == value) return;
            _showFpsStats = value;
            OnPropertyChanged();
        }
    }
}
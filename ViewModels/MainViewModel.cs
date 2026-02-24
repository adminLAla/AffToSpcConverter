using AffToSpcConverter.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AffToSpcConverter.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private const double PreviewMinPixelsPerSecond = 500.0;
    private const double PreviewMaxPixelsPerSecond = 1500.0;

    private string _status = "Ready.";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    // 映射规则
    private string _mappingRule = "自建规则";
    public string MappingRule { get => _mappingRule; set { _mappingRule = value; OnPropertyChanged(); } }

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

    // ---- 自建规则：参数映射 ----

    private string _noteLaneMapping = "direct";
    public string NoteLaneMapping { get => _noteLaneMapping; set { _noteLaneMapping = value; OnPropertyChanged(); } }

    private int _noteDefaultKind = 1;
    public int NoteDefaultKind { get => _noteDefaultKind; set { _noteDefaultKind = value; OnPropertyChanged(); } }

    private string _holdLaneMapping = "direct";
    public string HoldLaneMapping { get => _holdLaneMapping; set { _holdLaneMapping = value; OnPropertyChanged(); } }

    private int _holdDefaultWidth = 1;
    public int HoldDefaultWidth { get => _holdDefaultWidth; set { _holdDefaultWidth = value; OnPropertyChanged(); } }

    private bool _holdAllowNegativeDuration;
    public bool HoldAllowNegativeDuration { get => _holdAllowNegativeDuration; set { _holdAllowNegativeDuration = value; OnPropertyChanged(); } }

    private string _arcXMapping = "clamp01";
    public string ArcXMapping { get => _arcXMapping; set { _arcXMapping = value; OnPropertyChanged(); } }

    private bool _arcIgnoreY = true;
    public bool ArcIgnoreY { get => _arcIgnoreY; set { _arcIgnoreY = value; OnPropertyChanged(); } }

    private string _flickDirectionMode = "auto";
    public string FlickDirectionMode { get => _flickDirectionMode; set { _flickDirectionMode = value; OnPropertyChanged(); } }

    private int _flickFixedDir = 4;
    public int FlickFixedDir { get => _flickFixedDir; set { _flickFixedDir = value; OnPropertyChanged(); } }

    private string _flickWidthMode = "default";
    public string FlickWidthMode { get => _flickWidthMode; set { _flickWidthMode = value; OnPropertyChanged(); } }

    private int _flickFixedWidthNum = 6;
    public int FlickFixedWidthNum { get => _flickFixedWidthNum; set { _flickFixedWidthNum = value; OnPropertyChanged(); } }

    private int _flickWidthRandomMax = 12;
    public int FlickWidthRandomMax { get => _flickWidthRandomMax; set { _flickWidthRandomMax = value; OnPropertyChanged(); } }

    // ---- 高级设置 ----

    private int _globalTimeOffsetMs = 0;
    public int GlobalTimeOffsetMs { get => _globalTimeOffsetMs; set { _globalTimeOffsetMs = value; OnPropertyChanged(); } }

    private int _minHoldDurationMs = 0;
    public int MinHoldDurationMs { get => _minHoldDurationMs; set { _minHoldDurationMs = value; OnPropertyChanged(); } }

    private int _minSkyAreaDurationMs = 0;
    public int MinSkyAreaDurationMs { get => _minSkyAreaDurationMs; set { _minSkyAreaDurationMs = value; OnPropertyChanged(); } }

    private bool _outputBpmChanges;
    public bool OutputBpmChanges { get => _outputBpmChanges; set { _outputBpmChanges = value; OnPropertyChanged(); } }

    private int _deduplicateTapThresholdMs = 0;
    public int DeduplicateTapThresholdMs { get => _deduplicateTapThresholdMs; set { _deduplicateTapThresholdMs = value; OnPropertyChanged(); } }

    private string _sortMode = "timeFirst";
    public string SortMode { get => _sortMode; set { _sortMode = value; OnPropertyChanged(); } }

    private int _textEditUndoLimit = 200;
    public int TextEditUndoLimit
    {
        get => _textEditUndoLimit;
        set
        {
            int clamped = Math.Clamp(value, 1, 5000);
            if (_textEditUndoLimit == clamped) return;
            _textEditUndoLimit = clamped;
            OnPropertyChanged();
        }
    }

    // ---- 预览 ----
    private string _affPreview = "";
    public string AffPreview { get => _affPreview; set { _affPreview = value; OnPropertyChanged(); } }

    private string _spcPreview = "";
    public string SpcPreview { get => _spcPreview; set { _spcPreview = value; OnPropertyChanged(); } }

    public string? LoadedAffPath { get; set; }
    public string? GeneratedSpcText { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    // 触发属性变更通知。
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ---- 可视化预览 ----
    private IReadOnlyList<ISpcEvent>? _previewEvents;
    public IReadOnlyList<ISpcEvent>? PreviewEvents
    {
        get => _previewEvents;
        set
        {
            if (ReferenceEquals(_previewEvents, value)) return;
            _previewEvents = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanVisualPreview));
        }
    }

    // 是否已有可用于可视化预览的谱面事件数据。
    public bool CanVisualPreview => _previewEvents != null;

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

    private double _previewPixelsPerSecond = 1000;
    public double PreviewPixelsPerSecond
    {
        get => _previewPixelsPerSecond;
        set
        {
            double next = System.Math.Clamp(value, PreviewMinPixelsPerSecond, PreviewMaxPixelsPerSecond);
            if (System.Math.Abs(_previewPixelsPerSecond - next) < 0.0001) return;
            _previewPixelsPerSecond = next;
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
            // 播放速度仅允许 0.25x~1.5x，步进 0.05x。
            var next = System.Math.Clamp(System.Math.Round(value / 0.05) * 0.05, 0.25, 1.5);
            if (System.Math.Abs(_previewSpeed - next) < 0.0001) return;
            _previewSpeed = next;
            OnPropertyChanged();
        }
    }

    private int _previewSyncOffsetMs;
    public int PreviewSyncOffsetMs
    {
        get => _previewSyncOffsetMs;
        set
        {
            // 允许正负偏移，避免误输入极端值导致预览时间明显异常。
            int next = System.Math.Clamp(value, -5000, 5000);
            if (_previewSyncOffsetMs == next) return;
            _previewSyncOffsetMs = next;
            OnPropertyChanged();
        }
    }

    // 当前预览“流速”显示值（px/s）；不受播放速度倍率影响。
    public double PreviewPixelsPerSecondEffective => PreviewPixelsPerSecond;

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

    private int _previewTargetFps = 120;
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

    private bool _disableNoteSelectionWhilePlaying;
    public bool DisableNoteSelectionWhilePlaying
    {
        get => _disableNoteSelectionWhilePlaying;
        set
        {
            if (_disableNoteSelectionWhilePlaying == value) return;
            _disableNoteSelectionWhilePlaying = value;
            OnPropertyChanged();
        }
    }

    private string _previewNoteEditTriggerMode = "single";
    public string PreviewNoteEditTriggerMode
    {
        get => _previewNoteEditTriggerMode;
        set
        {
            var next = value == "double" ? "double" : "single";
            if (_previewNoteEditTriggerMode == next) return;
            _previewNoteEditTriggerMode = next;
            OnPropertyChanged();
        }
    }

    private bool _previewUseVsync = true;
    public bool PreviewUseVsync
    {
        get => _previewUseVsync;
        set
        {
            if (_previewUseVsync == value) return;
            _previewUseVsync = value;
            OnPropertyChanged();
        }
    }
}

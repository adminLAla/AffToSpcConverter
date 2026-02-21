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

    // ---- Optional features ----

    private bool _tapWidthPatternEnabled;
    public bool TapWidthPatternEnabled { get => _tapWidthPatternEnabled; set { _tapWidthPatternEnabled = value; OnPropertyChanged(); } }

    private string _tapWidthPattern = "1,2";
    public string TapWidthPattern { get => _tapWidthPattern; set { _tapWidthPattern = value; OnPropertyChanged(); } }

    private int _denseTapThresholdMs = 0; // 0 => auto (16th note from base bpm)
    public int DenseTapThresholdMs { get => _denseTapThresholdMs; set { _denseTapThresholdMs = value; OnPropertyChanged(); } }

    private bool _holdWidthRandomEnabled;
    public bool HoldWidthRandomEnabled { get => _holdWidthRandomEnabled; set { _holdWidthRandomEnabled = value; OnPropertyChanged(); } }

    private int _holdWidthRandomMax = 2;
    public int HoldWidthRandomMax { get => _holdWidthRandomMax; set { _holdWidthRandomMax = value; OnPropertyChanged(); } }

    private bool _skyareaStrategy2;
    public bool SkyareaStrategy2 { get => _skyareaStrategy2; set { _skyareaStrategy2 = value; OnPropertyChanged(); } }

    private int _randomSeed = 12345;
    public int RandomSeed { get => _randomSeed; set { _randomSeed = value; OnPropertyChanged(); } }

    // ---- Previews ----
    private string _affPreview = "";
    public string AffPreview { get => _affPreview; set { _affPreview = value; OnPropertyChanged(); } }

    private string _spcPreview = "";
    public string SpcPreview { get => _spcPreview; set { _spcPreview = value; OnPropertyChanged(); } }

    public string? LoadedAffPath { get; set; }
    public string? GeneratedSpcText { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AffToSpcConverter.Utils;

namespace AffToSpcConverter.ViewModels;

public sealed class BundleTexturePackageChartRowViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; OnPropertyChanged(); } }

    private int _chartSlotIndex;
    public int ChartSlotIndex { get => _chartSlotIndex; set { if (_chartSlotIndex == value) return; _chartSlotIndex = value; OnPropertyChanged(); } }

    private byte _difficultyFlag = 1;
    public byte DifficultyFlag { get => _difficultyFlag; set { if (_difficultyFlag == value) return; _difficultyFlag = value; OnPropertyChanged(); } }

    private byte _available = 1;
    public byte Available { get => _available; set { if (_available == value) return; _available = value; OnPropertyChanged(); } }

    private int _rating;
    public int Rating { get => _rating; set { if (_rating == value) return; _rating = value; OnPropertyChanged(); } }

    private string _levelSectionIndicator = "1";
    public string LevelSectionIndicator { get => _levelSectionIndicator; set { if (_levelSectionIndicator == value) return; _levelSectionIndicator = value; OnPropertyChanged(); } }

    private string _displayChartDesigner = "";
    public string DisplayChartDesigner { get => _displayChartDesigner; set { if (_displayChartDesigner == value) return; _displayChartDesigner = value; OnPropertyChanged(); } }

    private string _displayJacketDesigner = "";
    public string DisplayJacketDesigner { get => _displayJacketDesigner; set { if (_displayJacketDesigner == value) return; _displayJacketDesigner = value; OnPropertyChanged(); } }

    private string _chartFilePath = "";
    public string ChartFilePath { get => _chartFilePath; set { if (_chartFilePath == value) return; _chartFilePath = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class BundleTexturePackageViewModel : INotifyPropertyChanged
{
    private string _gameDirectory = "";
    public string GameDirectory { get => _gameDirectory; set { if (_gameDirectory == value) return; _gameDirectory = value; OnPropertyChanged(); } }

    private string _bundleFilePath = "";
    public string BundleFilePath { get => _bundleFilePath; set { if (_bundleFilePath == value) return; _bundleFilePath = value; OnPropertyChanged(); } }

    private string _sharedAssetsFilePath = "";
    public string SharedAssetsFilePath { get => _sharedAssetsFilePath; set { if (_sharedAssetsFilePath == value) return; _sharedAssetsFilePath = value; OnPropertyChanged(); } }

    private string _resourcesAssetsFilePath = "";
    public string ResourcesAssetsFilePath { get => _resourcesAssetsFilePath; set { if (_resourcesAssetsFilePath == value) return; _resourcesAssetsFilePath = value; OnPropertyChanged(); } }

    private string _jacketImageFilePath = "";
    public string JacketImageFilePath { get => _jacketImageFilePath; set { if (_jacketImageFilePath == value) return; _jacketImageFilePath = value; OnPropertyChanged(); } }

    private string _bgmFilePath = "";
    public string BgmFilePath { get => _bgmFilePath; set { if (_bgmFilePath == value) return; _bgmFilePath = value; OnPropertyChanged(); } }

    private string _outputDirectory = "";
    public string OutputDirectory { get => _outputDirectory; set { if (_outputDirectory == value) return; _outputDirectory = value; OnPropertyChanged(); } }

    private string _baseName = "";
    public string BaseName { get => _baseName; set { if (_baseName == value) return; _baseName = value; OnPropertyChanged(); } }

    private double _previewStartSeconds;
    public double PreviewStartSeconds { get => _previewStartSeconds; set { if (_previewStartSeconds == value) return; _previewStartSeconds = value; OnPropertyChanged(); } }

    private double _previewEndSeconds = 15;
    public double PreviewEndSeconds { get => _previewEndSeconds; set { if (_previewEndSeconds == value) return; _previewEndSeconds = value; OnPropertyChanged(); } }

    private string _displayNameSectionIndicator = "0";
    public string DisplayNameSectionIndicator { get => _displayNameSectionIndicator; set { if (_displayNameSectionIndicator == value) return; _displayNameSectionIndicator = value; OnPropertyChanged(); } }

    private string _displayArtistSectionIndicator = "0";
    public string DisplayArtistSectionIndicator { get => _displayArtistSectionIndicator; set { if (_displayArtistSectionIndicator == value) return; _displayArtistSectionIndicator = value; OnPropertyChanged(); } }

    private string _songTitleEnglish = "";
    public string SongTitleEnglish { get => _songTitleEnglish; set { if (_songTitleEnglish == value) return; _songTitleEnglish = value; OnPropertyChanged(); } }

    private string _songArtistEnglish = "";
    public string SongArtistEnglish { get => _songArtistEnglish; set { if (_songArtistEnglish == value) return; _songArtistEnglish = value; OnPropertyChanged(); } }

    private int _gameplayBackground;
    public int GameplayBackground { get => _gameplayBackground; set { if (_gameplayBackground == value) return; _gameplayBackground = value; OnPropertyChanged(); } }

    private int _rewardStyle;
    public int RewardStyle { get => _rewardStyle; set { if (_rewardStyle == value) return; _rewardStyle = value; OnPropertyChanged(); } }

    private SongDatabaseSlotInfo? _selectedSongSlot;
    public SongDatabaseSlotInfo? SelectedSongSlot { get => _selectedSongSlot; set { if (_selectedSongSlot == value) return; _selectedSongSlot = value; OnPropertyChanged(); } }

    private JacketTemplateCandidate? _selectedJacketTemplate;
    public JacketTemplateCandidate? SelectedJacketTemplate { get => _selectedJacketTemplate; set { if (_selectedJacketTemplate == value) return; _selectedJacketTemplate = value; OnPropertyChanged(); } }

    private bool _manualJacketTemplateSelection;
    public bool ManualJacketTemplateSelection
    {
        get => _manualJacketTemplateSelection;
        set { if (_manualJacketTemplateSelection == value) return; _manualJacketTemplateSelection = value; OnPropertyChanged(); }
    }

    private BundleTexturePackageChartRowViewModel? _selectedChartRow;
    public BundleTexturePackageChartRowViewModel? SelectedChartRow { get => _selectedChartRow; set { if (_selectedChartRow == value) return; _selectedChartRow = value; OnPropertyChanged(); } }

    public ObservableCollection<SongDatabaseSlotInfo> EmptySongSlots { get; } = new();
    public ObservableCollection<JacketTemplateCandidate> JacketTemplates { get; } = new();
    public ObservableCollection<BundleTexturePackageChartRowViewModel> ChartRows { get; } = new();

    private bool _autoRenameWhenTargetLocked = true;
    public bool AutoRenameWhenTargetLocked
    {
        get => _autoRenameWhenTargetLocked;
        set { if (_autoRenameWhenTargetLocked == value) return; _autoRenameWhenTargetLocked = value; OnPropertyChanged(); }
    }

    private bool _keepJacketOriginalSize = true;
    public bool KeepJacketOriginalSize
    {
        get => _keepJacketOriginalSize;
        set { if (_keepJacketOriginalSize == value) return; _keepJacketOriginalSize = value; OnPropertyChanged(); }
    }

    private string _status = "请先选择游戏目录（In Falsus Demo），程序会自动定位 bundle/sharedassets0.assets，并输出到 SongData 文件夹。";
    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

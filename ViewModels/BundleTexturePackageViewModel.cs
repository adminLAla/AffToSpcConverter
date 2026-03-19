using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using InFalsusSongPackStudio.Utils;

namespace InFalsusSongPackStudio.ViewModels;

// “打包谱面”窗口中的单条谱面分档行 ViewModel。
public sealed class BundleTexturePackageChartRowViewModel : INotifyPropertyChanged, IDataErrorInfo
{
    private int _chartSlotIndex;
    public int ChartSlotIndex { get => _chartSlotIndex; set { if (_chartSlotIndex == value) return; _chartSlotIndex = value; OnPropertyChanged(); } }

    private byte _difficultyFlag = 1;
    public byte DifficultyFlag { get => _difficultyFlag; set { if (_difficultyFlag == value) return; _difficultyFlag = value; OnPropertyChanged(); } }

    private byte _available = 1;
    public byte Available
    {
        get => _available;
        set
        {
            byte next = (byte)(value == 0 ? 0 : 1);
            if (_available == next) return;
            _available = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAvailable));
        }
    }

    public bool IsAvailable
    {
        get => Available != 0;
        set => Available = (byte)(value ? 1 : 0);
    }

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

    public static bool IsSupportedDifficultyFlag(byte value)
        => value is 1 or 2 or 4 or 8;

    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            if (columnName == nameof(DifficultyFlag) && !IsSupportedDifficultyFlag(DifficultyFlag))
                return "难度仅支持 1/2/4/8";

            return string.Empty;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    // 处理 Property Changed 相关事件。
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// “打包谱面”窗口总 ViewModel，保存表单输入、扫描结果与导出状态。
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

    private string _displayNameSectionIndicator = "A";
    public string DisplayNameSectionIndicator { get => _displayNameSectionIndicator; set { if (_displayNameSectionIndicator == value) return; _displayNameSectionIndicator = value; OnPropertyChanged(); } }

    private string _displayArtistSectionIndicator = "A";
    public string DisplayArtistSectionIndicator { get => _displayArtistSectionIndicator; set { if (_displayArtistSectionIndicator == value) return; _displayArtistSectionIndicator = value; OnPropertyChanged(); } }

    private string _songTitleEnglish = "";
    public string SongTitleEnglish { get => _songTitleEnglish; set { if (_songTitleEnglish == value) return; _songTitleEnglish = value; OnPropertyChanged(); } }

    private string _songArtistEnglish = "";
    public string SongArtistEnglish { get => _songArtistEnglish; set { if (_songArtistEnglish == value) return; _songArtistEnglish = value; OnPropertyChanged(); } }

    private int _gameplayBackground = 3;
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

    private string _status = "等待导出或操作日志。";
    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; OnPropertyChanged(); }
    }

    private string _operationGuide = "1. 导入游戏所在目录\n2. 导入曲绘\n3. 导入 BGM\n4. 继续完成其余信息后导出";
    public string OperationGuide
    {
        get => _operationGuide;
        set { if (_operationGuide == value) return; _operationGuide = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    // 处理 Property Changed 相关事件。
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

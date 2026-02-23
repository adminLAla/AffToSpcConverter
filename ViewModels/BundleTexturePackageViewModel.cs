using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AffToSpcConverter.Utils;

namespace AffToSpcConverter.ViewModels;

public class BundleTexturePackageViewModel : INotifyPropertyChanged
{
    private string _imageFilePath = "";
    public string ImageFilePath
    {
        get => _imageFilePath;
        set { if (_imageFilePath == value) return; _imageFilePath = value; OnPropertyChanged(); }
    }

    private string _bundleFilePath = "";
    public string BundleFilePath
    {
        get => _bundleFilePath;
        set { if (_bundleFilePath == value) return; _bundleFilePath = value; OnPropertyChanged(); }
    }

    private BundleTextureEntry? _selectedTexture;
    public BundleTextureEntry? SelectedTexture
    {
        get => _selectedTexture;
        set { if (_selectedTexture == value) return; _selectedTexture = value; OnPropertyChanged(); }
    }

    // 当前 bundle 内可替换的 Texture2D 列表。
    public ObservableCollection<BundleTextureEntry> TextureCandidates { get; } = new();

    private string _outputDirectory = "";
    public string OutputDirectory
    {
        get => _outputDirectory;
        set { if (_outputDirectory == value) return; _outputDirectory = value; OnPropertyChanged(); }
    }

    private bool _autoRenameWhenTargetLocked = true;
    public bool AutoRenameWhenTargetLocked
    {
        get => _autoRenameWhenTargetLocked;
        set { if (_autoRenameWhenTargetLocked == value) return; _autoRenameWhenTargetLocked = value; OnPropertyChanged(); }
    }

    private bool _verifyReadbackSha256 = true;
    public bool VerifyReadbackSha256
    {
        get => _verifyReadbackSha256;
        set { if (_verifyReadbackSha256 == value) return; _verifyReadbackSha256 = value; OnPropertyChanged(); }
    }

    private string _status = "请先导入未加密 .bundle 文件并选择目标纹理。";
    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // 触发属性变更通知。
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

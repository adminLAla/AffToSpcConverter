using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AffToSpcConverter.ViewModels;

// 打包加密资源窗口的 ViewModel。
public class PackageViewModel : INotifyPropertyChanged
{
    private string _sourceFilePath = "";
    public string SourceFilePath { get => _sourceFilePath; set { _sourceFilePath = value; OnPropertyChanged(); } }

    private string _originalFilename = "";
    public string OriginalFilename { get => _originalFilename; set { _originalFilename = value; OnPropertyChanged(); } }

    private string _selectedTargetLookupPath = "";
    public string SelectedTargetLookupPath { get => _selectedTargetLookupPath; set { _selectedTargetLookupPath = value; OnPropertyChanged(); } }

    // 映射表中过滤后的可替换目标路径列表（来自 FullLookupPath）。
    public ObservableCollection<string> TargetLookupCandidates { get; } = new();

    private string _outputDirectory = "";
    public string OutputDirectory { get => _outputDirectory; set { _outputDirectory = value; OnPropertyChanged(); } }

    private string _status = "Ready.";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    // 触发属性变更通知。
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

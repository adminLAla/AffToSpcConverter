using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AffToSpcConverter.ViewModels;

public class PackageViewModel : INotifyPropertyChanged
{
    private string _sourceFilePath = "";
    public string SourceFilePath { get => _sourceFilePath; set { _sourceFilePath = value; OnPropertyChanged(); } }

    private string _originalFilename = "";
    public string OriginalFilename { get => _originalFilename; set { _originalFilename = value; OnPropertyChanged(); } }

    private string _outputDirectory = "";
    public string OutputDirectory { get => _outputDirectory; set { _outputDirectory = value; OnPropertyChanged(); } }

    private string _status = "Ready.";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

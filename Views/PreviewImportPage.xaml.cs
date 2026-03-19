using System;
using System.Windows;
using System.Windows.Controls;

namespace InFalsusSongPackStudio.Views;

public partial class PreviewImportPage : UserControl
{
    public event Action? ImportSpcRequested;

    public PreviewImportPage()
    {
        InitializeComponent();
        SetStatus("尚未导入 SPC。", false);
    }

    public void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 60, 60))
            : (System.Windows.Media.Brush)(TryFindResource("AppSubtleForegroundBrush") ?? System.Windows.Media.Brushes.Gray);
    }

    private void ImportSpc_Click(object sender, RoutedEventArgs e)
        => ImportSpcRequested?.Invoke();
}

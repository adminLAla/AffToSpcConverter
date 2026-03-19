using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace InFalsusSongPackStudio.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = BuildVersionText();
    }

    private static string BuildVersionText()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            string normalized = informational.Split('+')[0];
            if (normalized.StartsWith("v", System.StringComparison.OrdinalIgnoreCase))
                return normalized;

            return $"v{normalized}";
        }

        var version = assembly.GetName().Version;
        return version is null ? "v0.9.0" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

using System.Windows;

namespace AffToSpcConverter.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
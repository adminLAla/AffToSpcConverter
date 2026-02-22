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

    private void OpenCustomMapping_Click(object sender, RoutedEventArgs e)
    {
        var win = new CustomMappingWindow { Owner = this, DataContext = this.DataContext };
        win.ShowDialog();
    }
}
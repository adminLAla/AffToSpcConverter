using System.Windows;

namespace AffToSpcConverter.Views;

public partial class CustomMappingWindow : Window
{
    public CustomMappingWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

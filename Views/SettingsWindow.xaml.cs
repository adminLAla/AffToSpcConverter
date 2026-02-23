using System.Windows;

namespace AffToSpcConverter.Views;

public partial class SettingsWindow : Window
{
    // 初始化设置窗口。
    public SettingsWindow()
    {
        InitializeComponent();
    }

    // 保存设置窗口中的修改并关闭窗口。
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    // 打开自定义映射配置窗口。
    private void OpenCustomMapping_Click(object sender, RoutedEventArgs e)
    {
        var win = new CustomMappingWindow { Owner = this, DataContext = this.DataContext };
        win.ShowDialog();
    }
}
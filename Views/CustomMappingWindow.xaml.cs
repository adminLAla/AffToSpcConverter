using System.Windows;

namespace AffToSpcConverter.Views;

public partial class CustomMappingWindow : Window
{
    // 初始化自定义映射窗口。
    public CustomMappingWindow()
    {
        InitializeComponent();
    }

    // 确认自定义映射文本并关闭窗口。
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

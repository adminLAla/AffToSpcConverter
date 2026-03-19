using System.Windows;
using System.Windows.Input;
using System.Linq;

namespace InFalsusSongPackStudio.Views;

public partial class AppPromptDialog : Window
{
    public AppPromptDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "提示" : title;
        MessageText.Text = message ?? string.Empty;
    }

    public static void Show(Window? owner, string title, string message)
    {
        var dialog = new AppPromptDialog(title, message);

        Window? resolvedOwner = null;
        if (owner != null && owner.IsVisible)
        {
            resolvedOwner = owner;
        }
        else
        {
            resolvedOwner = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsVisible && w.IsActive);
        }

        if (resolvedOwner != null)
            dialog.Owner = resolvedOwner;

        dialog.ShowDialog();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}

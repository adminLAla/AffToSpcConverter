using System;
using System.Windows;
using System.Windows.Controls;

namespace AffToSpcConverter.Views;

// 新增音符对话框，用于录入预览中插入音符的参数。
public partial class AddNoteDialog : Window
{
    public string SelectedType { get; private set; } = "Tap";
    public int Kind { get; private set; } = 1;
    public int Den { get; private set; } = 24;
    public int WidthNum { get; private set; } = 1;
    public int Dir { get; private set; } = 4;
    public int WidthNum2 { get; private set; } = 1;
    public int LeftEase { get; private set; }
    public int RightEase { get; private set; }
    public int GroupId { get; private set; } = 1;

    // 初始化添加音符对话框并填充类型与默认分母。
    public AddNoteDialog(string[] typeNames, int defaultDen)
    {
        InitializeComponent();
        foreach (var name in typeNames)
            CbType.Items.Add(name);
        CbType.SelectedIndex = 0;
        TbDen.Text = Math.Max(1, defaultDen).ToString();
    }

    // 根据音符类型切换新增音符表单字段。
    private void CbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CbType.SelectedItem == null) return;
        string type = CbType.SelectedItem.ToString()!;

        bool isGround = type == "Tap" || type == "Hold";
        bool isSky = type == "Flick" || type == "SkyArea";
        bool isFlick = type == "Flick";
        bool isSkyArea = type == "SkyArea";

        PanelGround.Visibility = isGround ? Visibility.Visible : Visibility.Collapsed;
        PanelSky.Visibility = isSky ? Visibility.Visible : Visibility.Collapsed;
        PanelFlick.Visibility = isFlick ? Visibility.Visible : Visibility.Collapsed;
        PanelSkyArea.Visibility = isSkyArea ? Visibility.Visible : Visibility.Collapsed;
    }

    // 校验输入并确认新增音符参数。
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedType = CbType.SelectedItem?.ToString() ?? "Tap";

        switch (SelectedType)
        {
            case "Tap":
                if (!int.TryParse(TbKind.Text, out int tk)) { MessageBox.Show("Kind / Width 值无效。"); return; }
                Kind = Math.Clamp(tk, 1, 4);
                break;

            case "Hold":
                if (!int.TryParse(TbKind.Text, out int hk)) { MessageBox.Show("Kind / Width 值无效。"); return; }
                Kind = Math.Clamp(hk, 1, 6);
                break;

            case "Flick":
                if (!int.TryParse(TbDen.Text, out int fd)) { MessageBox.Show("Den 值无效。"); return; }
                if (!int.TryParse(TbWidthNum.Text, out int fw)) { MessageBox.Show("WidthNum 值无效。"); return; }
                Den = Math.Max(1, fd);
                WidthNum = Math.Max(1, fw);
                WidthNum2 = WidthNum;
                var dirItem = CbDir.SelectedItem as ComboBoxItem;
                Dir = dirItem?.Content?.ToString() == "16" ? 16 : 4;
                break;

            case "SkyArea":
                if (!int.TryParse(TbDen.Text, out int sd)) { MessageBox.Show("Den 值无效。"); return; }
                if (!int.TryParse(TbWidthNum.Text, out int sw)) { MessageBox.Show("WidthNum 值无效。"); return; }
                Den = Math.Max(1, sd);
                WidthNum = Math.Max(0, sw);
                WidthNum2 = WidthNum;
                LeftEase = 0;
                RightEase = 0;
                GroupId = 1;
                break;
        }

        DialogResult = true;
        Close();
    }
}

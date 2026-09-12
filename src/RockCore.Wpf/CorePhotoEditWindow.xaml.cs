using System.Windows;
using RockCore.Wpf.ViewModels;

namespace RockCore.Wpf;

/// <summary>
/// 物理段编辑窗口。允许用户修正箱号与深度范围，并可选标记为"用户修正"
/// 以便后续的图像分析阶段不覆盖本次修改。
/// </summary>
public partial class CorePhotoEditWindow : Window
{
    private readonly CorePhotoViewModel _photo;

    public bool WasModified { get; private set; }

    public CorePhotoEditWindow(CorePhotoViewModel photo)
    {
        InitializeComponent();
        _photo = photo;

        Title = $"编辑物理段 - {photo.FileName}";
        FileNameTextBox.Text = photo.FileName;
        BoxNumberTextBox.Text = photo.BoxNumber.ToString();
        DepthStartTextBox.Text = photo.DepthStart.ToString("F2");
        DepthEndTextBox.Text = photo.DepthEnd.ToString("F2");
        MarkUserModifiedCheckBox.IsChecked = photo.IsUserModified;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BoxNumberTextBox.Text, out var box))
        {
            MessageBox.Show(this, "箱号必须为整数。", "输入错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!double.TryParse(DepthStartTextBox.Text, out var start) ||
            !double.TryParse(DepthEndTextBox.Text, out var end))
        {
            MessageBox.Show(this, "深度起终点必须为数字。", "输入错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (end <= start)
        {
            MessageBox.Show(this, "深度终点必须大于起点。", "输入错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _photo.BoxNumber = box;
        _photo.DepthStart = start;
        _photo.DepthEnd = end;
        _photo.IsUserModified = MarkUserModifiedCheckBox.IsChecked == true;
        WasModified = true;

        DialogResult = true;
        Close();
    }
}

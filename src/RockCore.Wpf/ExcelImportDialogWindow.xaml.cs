using System.IO;
using System.Windows;
using Microsoft.Win32;
using RockCore.Infrastructure.Services;
using RockCore.Wpf.ViewModels;

namespace RockCore.Wpf;

/// <summary>
/// 岩心数据表（Excel 模板 v4）导入对话框：选择文件与目标钻孔，预览校验与判级结果，确认后导入。
/// </summary>
public partial class ExcelImportDialogWindow : Window
{
    private readonly ExcelImportService _service;
    private ExcelImportPreview? _preview;

    /// <summary>导入成功的目标钻孔（主窗口据此决定是否刷新分段表）。</summary>
    public int? ImportedBoreholeId { get; private set; }
    public string? ImportedBoreholeNumber { get; private set; }

    public ExcelImportDialogWindow(
        ExcelImportService service,
        IEnumerable<BoreholeViewModel> boreholes,
        BoreholeViewModel? preselect)
    {
        InitializeComponent();
        _service = service;

        var list = boreholes.ToList();
        BoreholeCombo.ItemsSource = list;
        if (preselect != null)
            BoreholeCombo.SelectedItem = list.FirstOrDefault(b => b.Id == preselect.Id);
        if (BoreholeCombo.SelectedItem == null && list.Count == 1)
            BoreholeCombo.SelectedIndex = 0;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择岩心数据表",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        FilePathBox.Text = dialog.FileName;
        LoadPreview(dialog.FileName);

        // 文件名与钻孔编号自动预匹配（如 ZK1.xlsx → ZK1）
        if (BoreholeCombo.SelectedItem == null && BoreholeCombo.ItemsSource is IEnumerable<BoreholeViewModel> list)
        {
            var name = Path.GetFileNameWithoutExtension(dialog.FileName).Trim();
            var match = list.FirstOrDefault(b =>
                string.Equals(b.Number.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (match != null) BoreholeCombo.SelectedItem = match;
        }
    }

    private void LoadPreview(string filePath)
    {
        _preview = null;
        ImportButton.IsEnabled = false;

        try
        {
            _preview = _service.Preview(filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"解析失败：{ex.Message}", "导入预览",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_preview.Error != null)
        {
            PreviewGrid.ItemsSource = null;
            SummaryText.Text = _preview.Error;
            return;
        }

        PreviewGrid.ItemsSource = _preview.Rows;
        SummaryText.Text =
            $"共 {_preview.Rows.Count} 行：正常 {_preview.OkCount}，" +
            $"警告 {_preview.WarnCount}（黄行，可勾选仅导入正常行排除），" +
            $"阻断 {_preview.BlockCount}（红行，始终排除）。" +
            "深度按行序自动累计，完整性按表 F.0.4 判定。";
        ImportButton.IsEnabled = _preview.Rows.Any(r => r.Status != ImportRowStatus.Blocked);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_preview == null || _preview.Error != null) return;
        if (BoreholeCombo.SelectedItem is not BoreholeViewModel borehole)
        {
            MessageBox.Show(this, "请选择目标钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(StartDepthBox.Text.Trim(), out var startDepth) || startDepth < 0)
        {
            MessageBox.Show(this, "起始深度必须是不小于 0 的数字", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = await _service.ImportAsync(
                _preview.FilePath, borehole.Id, startDepth, OnlyOkCheck.IsChecked == true);
            if (!result.Success)
            {
                MessageBox.Show(this, result.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ImportedBoreholeId = borehole.Id;
            ImportedBoreholeNumber = borehole.Number;
            MessageBox.Show(this,
                $"{result.Message}（钻孔 {borehole.Number}）",
                "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导入失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

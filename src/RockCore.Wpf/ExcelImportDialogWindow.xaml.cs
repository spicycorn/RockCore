using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using RockCore.Infrastructure.Services;
using RockCore.Wpf.ViewModels;

namespace RockCore.Wpf;

/// <summary>
/// 岩心数据表（Excel 模板 v4）导入对话框：选择文件与目标钻孔，预览校验与判级结果，确认后导入。
/// </summary>
public partial class ExcelImportDialogWindow : Window
{
    private static readonly Brush ErrorBrush = Brushes.Firebrick;

    private readonly ExcelImportService _service;
    private ExcelImportPreview? _preview;

    /// <summary>导入成功的目标钻孔（主窗口据此决定是否刷新分段表）。</summary>
    public int? ImportedBoreholeId { get; private set; }
    public string? ImportedBoreholeNumber { get; private set; }
    /// <summary>本次实际导入的回次数（主窗口状态栏展示）。</summary>
    public int ImportedRowCount { get; private set; }

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

    private void DownloadTemplate_Click(object sender, RoutedEventArgs e)
        => ImportTemplateProvider.DownloadWithDialog(this);

    private void LoadPreview(string filePath)
    {
        _preview = null;
        ImportButton.IsEnabled = false;
        SummaryText.Foreground = (Brush)FindResource("TextSecondaryBrush");

        try
        {
            _preview = _service.Preview(filePath);
        }
        catch (Exception ex)
        {
            ShowPreviewError($"解析失败：{ex.Message}");
            return;
        }

        if (_preview.Error != null)
        {
            ShowPreviewError(_preview.Error);
            return;
        }

        PreviewGrid.ItemsSource = _preview.Rows;
        SegmentGrid.ItemsSource = _preview.Segments;
        SummaryText.Text =
            $"共 {_preview.Rows.Count} 行：正常 {_preview.OkCount}，" +
            $"警告 {_preview.WarnCount}（黄行，可勾选仅导入正常行排除），" +
            $"阻断 {_preview.BlockCount}（红行，始终排除）。" +
            $"{_preview.SegmentNote}。判级只按平均间距查表 F.0.4，深度按行序自动累计。";
        ImportButton.IsEnabled = _preview.Rows.Any(r => r.Status != ImportRowStatus.Blocked);

        if (!ImportButton.IsEnabled)
            ShowPreviewError($"全部 {_preview.Rows.Count} 行均被阻断（红行），没有可导入的数据。请修正红行的问题后重新选择文件。");
    }

    /// <summary>
    /// 预览失败时把原因同时写到底部摘要（红字）并弹窗——只写一行灰字用户会以为"按钮没反应"。
    /// </summary>
    private void ShowPreviewError(string message)
    {
        PreviewGrid.ItemsSource = null;
        SegmentGrid.ItemsSource = null;
        ImportButton.IsEnabled = false;
        SummaryText.Foreground = ErrorBrush;
        SummaryText.Text = "✗ " + message;
        MessageBox.Show(this,
            message + "\n\n请确认：\n" +
            "① 使用的是最新版《岩心回次统计导入模板》（可用「下载导入模板」获取）；\n" +
            "② 数据填在「数据录入」表，一行一个回次，从第 2 行开始；\n" +
            "③ 「回次进尺(m)」为大于 0 的数字（单位 m，不是 cm）。",
            "无法预览", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(this,
                    result.Message + "\n\n请检查：\n" +
                    "① 目标钻孔是否选对（导入会替换该钻孔此前的导入批次）；\n" +
                    "② 是否勾选了「仅导入正常行」而当前全部行都是警告行；\n" +
                    "③ 红行（阻断）需先在 Excel 中修正。",
                    "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ImportedBoreholeId = borehole.Id;
            ImportedBoreholeNumber = borehole.Number;
            ImportedRowCount = result.ImportedCount;

            var withDepth = _preview.Rows.Where(r => r.RunLengthM > 0).ToList();
            var span = withDepth.Count > 0
                ? $"\n深度区间：{withDepth.Min(r => r.DepthStart):0.00} → {withDepth.Max(r => r.DepthEnd):0.00} m"
                : string.Empty;
            MessageBox.Show(this,
                $"{result.Message}（钻孔 {borehole.Number}）{span}\n\n" +
                "完整性分段按归并后的岩体段生成，可在「岩心编辑器 → 完整性分段」中查看并补填 F.0.2 参数。",
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

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Core.Services;

namespace RockCore.Wpf;

/// <summary>
/// 照片导入对话框。扫描目录、解析文件名、允许用户修正后批量写入 CorePhotos。
/// </summary>
public partial class PhotoImportDialogWindow : Window
{
    private readonly Borehole _borehole;
    private readonly PhotoImportService _importService;
    private readonly ICorePhotoRepository _corePhotoRepository;
    private readonly ObservableCollection<PhotoImportRow> _rows = new();

    public int ImportedCount { get; private set; }

    public PhotoImportDialogWindow(
        Borehole borehole,
        PhotoImportService importService,
        ICorePhotoRepository corePhotoRepository)
    {
        InitializeComponent();
        _borehole = borehole;
        _importService = importService;
        _corePhotoRepository = corePhotoRepository;

        Title = $"导入岩芯照片 - {borehole.Number} (孔深 {borehole.TotalDepth:F1}m)";
        PhotosDataGrid.ItemsSource = _rows;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含岩芯照片的目录"
        };

        if (dialog.ShowDialog(this) == true)
        {
            DirectoryPathTextBox.Text = dialog.FolderName;
        }
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        var path = DirectoryPathTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            MessageBox.Show(this, "请选择有效的照片目录。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var results = _importService.ScanDirectory(path, _borehole);
            _rows.Clear();
            foreach (var r in results)
            {
                _rows.Add(new PhotoImportRow
                {
                    IsSelected = r.ParseSucceeded,
                    FileName = r.FileName,
                    FullPath = r.FullPath,
                    BoxNumber = r.BoxNumber,
                    DepthStart = r.DepthStart,
                    DepthEnd = r.DepthEnd,
                    Length = r.DepthEnd - r.DepthStart,
                    Warning = string.IsNullOrEmpty(r.ValidationWarning)
                        ? (r.ParseSucceeded ? string.Empty : r.ParseMessage)
                        : r.ValidationWarning
                });
            }

            var total = _rows.Count;
            var selected = _rows.Count(x => x.IsSelected);
            var warningCount = _rows.Count(x => !string.IsNullOrEmpty(x.Warning));
            SummaryTextBlock.Text =
                $"共找到 {total} 个文件，默认选中 {selected} 个；存在警告 {warningCount} 个。请核对箱号与深度，可双击单元格手动修正。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"扫描失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var accepted = _rows.Where(r => r.IsSelected).ToList();
        if (accepted.Count == 0)
        {
            MessageBox.Show(this, "没有选择任何照片。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 手动修正后的简单校验
        foreach (var row in accepted)
        {
            if (row.DepthEnd <= row.DepthStart)
            {
                MessageBox.Show(this, $"照片「{row.FileName}」的深度范围无效（起终点相同或反转）。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        try
        {
            if (ReplaceExistingCheckBox.IsChecked == true)
            {
                await _corePhotoRepository.DeleteByBoreholeIdAsync(_borehole.Id);
            }

            var parseResults = accepted.Select(r => new PhotoParseResult
            {
                FileName = r.FileName,
                FullPath = r.FullPath,
                BoxNumber = r.BoxNumber,
                DepthStart = r.DepthStart,
                DepthEnd = r.DepthEnd,
                ParseSucceeded = true
            }).ToList();

            var imported = await _importService.ImportAsync(_borehole.Id, parseResults);

            ImportedCount = imported.Count;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导入失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// 对话框行数据（不依赖 CommunityToolkit.Mvvm 的 ObservableProperty 源生成器以保持简单）。
/// </summary>
public class PhotoImportRow
{
    public bool IsSelected { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public int BoxNumber { get; set; }

    public double DepthStart { get; set; }

    public double DepthEnd { get; set; }

    public double Length { get; set; }

    public string Warning { get; set; } = string.Empty;
}

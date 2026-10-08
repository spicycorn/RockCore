using System.Windows;
using RockCore.Core.Models;
using RockCore.Infrastructure.Services;
using RockCore.Wpf.ViewModels;

namespace RockCore.Wpf;

/// <summary>
/// 数据源管理：查看/删除指定钻孔的 Excel 导入批次（删除批次同时删除其生成的完整性分段）。
/// </summary>
public partial class DataSourceManagerWindow : Window
{
    private readonly ExcelImportService _service;
    private readonly BoreholeViewModel _borehole;

    /// <summary>批次被删除后置 true，主窗口据此刷新分段表。</summary>
    public bool DeletedAnything { get; private set; }

    public DataSourceManagerWindow(ExcelImportService service, BoreholeViewModel borehole)
    {
        InitializeComponent();
        _service = service;
        _borehole = borehole;
        BoreholeTitle.Text = $"钻孔 {borehole.Number} 的导入批次";
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            BatchGrid.ItemsSource = await _service.GetBatchesAsync(_borehole.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"读取批次失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (BatchGrid.SelectedItem is not ImportBatch batch)
        {
            MessageBox.Show(this, "请先选择要删除的批次", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var confirm = MessageBox.Show(this,
            $"确定删除批次「{batch.FileName}」（{batch.RowCount} 个回次）？\n该批次生成的完整性分段将同时删除。",
            "删除批次", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _service.DeleteBatchAsync(batch.Id);
            DeletedAnything = true;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"删除失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

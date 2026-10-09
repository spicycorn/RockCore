using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace RockCore.Wpf;

/// <summary>
/// 《岩心回次统计导入模板》的软件内下载入口。
/// 模板 xlsx 以嵌入资源随程序集分发（见 RockCore.Wpf.csproj 的 EmbeddedResource），
/// 用户在软件内另存到任意目录即可，无需联网、不依赖安装目录是否带有模板文件。
/// </summary>
public static class ImportTemplateProvider
{
    /// <summary>嵌入资源的逻辑名（与 csproj 中 LogicalName 保持一致）。</summary>
    private const string ResourceName = "RockCore.Wpf.Assets.Templates.CoreRunTemplate.xlsx";

    /// <summary>另存为对话框中的默认文件名（与 docs\templates\ 下的模板同名）。</summary>
    public const string DefaultFileName = "岩心回次统计导入模板.xlsx";

    /// <summary>读取嵌入资源里的模板内容。</summary>
    public static byte[] ReadTemplateBytes()
    {
        var assembly = typeof(ImportTemplateProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            throw new InvalidOperationException($"内置模板资源缺失：{ResourceName}");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// 弹出「另存为」对话框，把模板写入用户选定的路径。
    /// </summary>
    /// <param name="owner">对话框宿主窗口。</param>
    /// <returns>保存后的完整路径；用户取消返回 null。</returns>
    public static string? SaveTemplate(Window owner)
    {
        var dialog = new SaveFileDialog
        {
            Title = "下载岩心回次统计导入模板",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = DefaultFileName,
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(owner) != true) return null;

        File.WriteAllBytes(dialog.FileName, ReadTemplateBytes());
        return dialog.FileName;
    }

    /// <summary>
    /// 下载全流程：另存为 + 结果提示（可一键在资源管理器中定位）。
    /// </summary>
    /// <param name="owner">对话框宿主窗口。</param>
    /// <returns>模板是否已写出（用户取消或写盘失败返回 false）。</returns>
    public static bool DownloadWithDialog(Window owner)
    {
        string? saved;
        try
        {
            saved = SaveTemplate(owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"模板保存失败：{ex.Message}", "下载导入模板",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        if (saved == null) return false;   // 用户取消

        if (MessageBox.Show(owner,
                $"模板已保存：\n{saved}\n\n" +
                "首次使用请先看模板内「填写说明」表；前 5 行是样板数据，正式填写前请删除。\n\n" +
                "是否打开所在文件夹？",
                "下载导入模板", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            ShowInFolder(saved);
        }

        return true;
    }

    /// <summary>在资源管理器中选中该文件（定位失败静默忽略，不影响已保存的模板）。</summary>
    public static void ShowInFolder(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 忽略：定位失败不影响模板已保存的事实
        }
    }
}

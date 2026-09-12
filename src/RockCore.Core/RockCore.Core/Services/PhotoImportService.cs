using RockCore.Core.Interfaces;
using RockCore.Core.Models;

namespace RockCore.Core.Services;

/// <summary>
/// 照片导入服务。负责：
///   1. 解析给定目录下全部照片文件名
///   2. 进行区间重叠与越界校验
///   3. 转换为 CorePhoto 物理段并批量写入
///   4. 复制照片文件到钻孔工作目录，便于后续图像识别阶段读取
/// </summary>
public class PhotoImportService
{
    private readonly ICorePhotoRepository _corePhotoRepository;

    public PhotoImportService(ICorePhotoRepository corePhotoRepository)
    {
        _corePhotoRepository = corePhotoRepository;
    }

    /// <summary>
    /// 扫描目录并解析文件名，不写入数据库。用于导入预览。
    /// </summary>
    public List<PhotoParseResult> ScanDirectory(string directoryPath, Borehole borehole)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"找不到目录：{directoryPath}");

        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif"
        };

        var files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = files.Select(PhotoFileNameParser.Parse).ToList();

        ValidateAndAnnotate(results, borehole);

        return results;
    }

    /// <summary>
    /// 根据解析结果创建 CorePhoto 记录。
    /// </summary>
    /// <param name="boreholeId">钻孔 Id</param>
    /// <param name="parseResults">用户确认后的解析结果（可含用户手动修正）</param>
    public async Task<List<CorePhoto>> ImportAsync(
        int boreholeId,
        IEnumerable<PhotoParseResult> parseResults)
    {
        var accepted = parseResults.Where(p => p.ParseSucceeded).ToList();

        if (accepted.Count == 0)
            throw new InvalidOperationException("没有可导入的照片。");

        var corePhotos = accepted.Select(p => new CorePhoto
        {
            BoreholeId = boreholeId,
            FileName = p.FileName,
            RelativePath = p.FullPath,
            DepthStart = p.DepthStart,
            DepthEnd = p.DepthEnd,
            BoxNumber = p.BoxNumber,
            AnalysisStatus = "未分析"
        }).ToList();

        await _corePhotoRepository.AddRangeAsync(corePhotos);
        return corePhotos;
    }

    /// <summary>
    /// 对解析结果进行区间重叠、越界校验，并写入 ValidationWarning。
    /// </summary>
    private static void ValidateAndAnnotate(List<PhotoParseResult> results, Borehole borehole)
    {
        var validSegments = results
            .Where(r => r.ParseSucceeded)
            .OrderBy(r => r.DepthStart)
            .ToList();

        // 1. 越界校验
        foreach (var r in validSegments)
        {
            if (r.DepthEnd > borehole.TotalDepth && borehole.TotalDepth > 0)
            {
                r.ValidationWarning = string.IsNullOrEmpty(r.ValidationWarning)
                    ? $"终点 {r.DepthEnd:F2}m 超过钻孔总深 {borehole.TotalDepth:F2}m"
                    : $"{r.ValidationWarning}；终点超过钻孔总深";
            }

            if (r.DepthStart < 0)
            {
                r.ValidationWarning = string.IsNullOrEmpty(r.ValidationWarning)
                    ? "起点为负数"
                    : $"{r.ValidationWarning}；起点为负数";
            }
        }

        // 2. 重叠 / 相邻间隙校验
        for (var i = 0; i < validSegments.Count; i++)
        {
            if (i + 1 >= validSegments.Count) continue;

            var current = validSegments[i];
            var next = validSegments[i + 1];

            if (next.DepthStart < current.DepthEnd - 0.0001)
            {
                var overlapMsg = $"与「{next.FileName}」深度范围重叠";
                current.ValidationWarning = string.IsNullOrEmpty(current.ValidationWarning)
                    ? overlapMsg
                    : $"{current.ValidationWarning}；{overlapMsg}";
            }
            else if (next.DepthStart - current.DepthEnd > 0.05)
            {
                var gap = next.DepthStart - current.DepthEnd;
                var gapMsg = $"与下一张存在 {gap:F2}m 间隙";
                next.ValidationWarning = string.IsNullOrEmpty(next.ValidationWarning)
                    ? gapMsg
                    : $"{next.ValidationWarning}；{gapMsg}";
            }
        }
    }
}


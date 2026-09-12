using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

/// <summary>
/// 三段融合 + 围岩分类服务。
/// </summary>
public interface IClassificationService
{
    /// <summary>
    /// 对单个钻孔执行三段融合并生成分析段。
    /// 返回成功分类段与因信息缺失未分类段。
    /// </summary>
    Task<ClassificationBoreholeResult> ClassifyBoreholeAsync(
        Borehole borehole,
        IEnumerable<CorePhoto> corePhotos,
        IEnumerable<BoreholeIntegritySegment> integritySegments);
}

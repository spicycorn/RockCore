using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;

namespace RockCore.Core.Services;

/// <summary>
/// 阶段五围岩分类服务：直接遍历阶段四填写的完整性分段，查表 F.0.2 得到围岩类别。
/// 每个 BoreholeIntegritySegment 对应一个分析段，不再做三段融合。
/// 信息缺失时明确提示用户补充，不伪造、不保守降级。
/// </summary>
public class ClassificationService : IClassificationService
{
    private readonly IClassificationSegmentRepository _classificationSegmentRepository;

    public ClassificationService(IClassificationSegmentRepository classificationSegmentRepository)
    {
        _classificationSegmentRepository = classificationSegmentRepository;
    }

    public async Task<ClassificationBoreholeResult> ClassifyBoreholeAsync(
        Borehole borehole,
        IEnumerable<CorePhoto> corePhotos,
        IEnumerable<BoreholeIntegritySegment> integritySegments)
    {
        if (borehole == null)
            throw new ArgumentNullException(nameof(borehole));

        var segments = integritySegments.OrderBy(s => s.DepthStart).ToList();

        if (segments.Count == 0)
            return new ClassificationBoreholeResult();

        // 诊断信息
        var segWithRockType = segments.Count(s => s.RockType != RockType.NotSet);
        var segWithStructure = segments.Count(s => s.RockStructureType != RockStructureType.NotSet);
        var missingRockTypeByLevel = segments
            .Where(s => s.RockType == RockType.NotSet)
            .GroupBy(s => s.IntegrityLevel.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new ClassificationBoreholeResult
        {
            TotalInfoSegments = segments.Count,
            InfoSegmentsWithRockType = segWithRockType,
            InfoSegmentsWithStructure = segWithStructure,
            MissingRockTypeByLevel = missingRockTypeByLevel
        };

        // 直接遍历每个信息段，逐段查表分类
        foreach (var seg in segments)
        {
            // 1. 检查完整性等级
            var integrityLevel = seg.IntegrityLevel;
            if (integrityLevel == IntegrityLevel.UserOverride || integrityLevel == IntegrityLevel.Unknown)
            {
                result.UnclassifiedSegments.Add(new UnclassifiedSegment
                {
                    BoreholeId = borehole.Id,
                    DepthStart = seg.DepthStart,
                    DepthEnd = seg.DepthEnd,
                    Hint = $"{seg.DepthStart:F2}m-{seg.DepthEnd:F2}m 完整性等级未设置，无法查表",
                    MissingFields = new List<string> { "完整性等级" }
                });
                continue;
            }

            // 2. 检查岩质类型
            if (seg.RockType == RockType.NotSet)
            {
                result.UnclassifiedSegments.Add(new UnclassifiedSegment
                {
                    BoreholeId = borehole.Id,
                    DepthStart = seg.DepthStart,
                    DepthEnd = seg.DepthEnd,
                    Hint = $"{seg.DepthStart:F2}m-{seg.DepthEnd:F2}m 岩质类型未设置，请选择硬质岩或软质岩",
                    MissingFields = new List<string> { "岩质类型" }
                });
                continue;
            }

            // 3. 检查岩体结构类型
            if (seg.RockStructureType == RockStructureType.NotSet)
            {
                result.UnclassifiedSegments.Add(new UnclassifiedSegment
                {
                    BoreholeId = borehole.Id,
                    DepthStart = seg.DepthStart,
                    DepthEnd = seg.DepthEnd,
                    Hint = $"{seg.DepthStart:F2}m-{seg.DepthEnd:F2}m 岩体结构类型未设置",
                    MissingFields = new List<string> { "岩体结构类型" }
                });
                continue;
            }

            // 4. 查表判定围岩类别
            var classification = RockClassificationTable.Classify(
                seg.RockType,
                seg.RockStructureType,
                integrityLevel,
                seg.GroundwaterCondition,
                seg.RockHardnessLevel,
                seg.RockHomogeneity,
                seg.CaveAxisAngleLessThan30);

            if (!classification.Success)
            {
                result.UnclassifiedSegments.Add(new UnclassifiedSegment
                {
                    BoreholeId = borehole.Id,
                    DepthStart = seg.DepthStart,
                    DepthEnd = seg.DepthEnd,
                    Hint = $"{seg.DepthStart:F2}m-{seg.DepthEnd:F2}m：{classification.Basis}",
                    MissingFields = classification.MissingFields
                });
                continue;
            }

            // 5. 成功分类
            result.ClassifiedSegments.Add(new ClassificationSegment
            {
                BoreholeId = borehole.Id,
                DepthStart = seg.DepthStart,
                DepthEnd = seg.DepthEnd,
                RockClass = classification.RockClass,
                IntegrityLevel = integrityLevel,
                RockType = seg.RockType,
                RockStructureType = seg.RockStructureType,
                GroundwaterCondition = seg.GroundwaterCondition,
                ConfidenceScore = 1.0,
                Basis = classification.Basis
            });
        }

        // 持久化：先删除旧分析段，再写入新分析段
        await _classificationSegmentRepository.DeleteByBoreholeIdAsync(borehole.Id);
        if (result.ClassifiedSegments.Count > 0)
        {
            await _classificationSegmentRepository.AddRangeAsync(result.ClassifiedSegments);
        }

        return result;
    }
}

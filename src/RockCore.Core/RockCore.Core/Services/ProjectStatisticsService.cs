using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Core.Services;

/// <summary>
/// 项目级围岩统计服务。
/// 占比按各钻孔岩心总长度（TotalDepth）计算，不是仅按已分类长度计算。
/// </summary>
public class ProjectStatisticsService
{
    /// <summary>
    /// 汇总多个钻孔的分析段统计信息。
    /// </summary>
    public ProjectStatisticsResult Compute(Project project, IEnumerable<Borehole> boreholes, IEnumerable<ClassificationSegment> segments)
    {
        if (project == null)
            throw new ArgumentNullException(nameof(project));

        var boreholeList = boreholes.ToList();
        var segmentList = segments.ToList();
        var boreholeDict = boreholeList.ToDictionary(b => b.Id, b => b);

        // 项目岩心总长度：各钻孔 TotalDepth 之和
        double totalCoreLength = boreholeList.Sum(b => b.TotalDepth);
        if (totalCoreLength <= 0)
            totalCoreLength = segmentList.Sum(s => s.DepthEnd - s.DepthStart);

        var result = new ProjectStatisticsResult
        {
            ProjectId = project.Id,
            TotalCoreLength = totalCoreLength,
            TotalClassifiedLength = segmentList.Sum(s => s.DepthEnd - s.DepthStart)
        };
        result.TotalUnclassifiedLength = Math.Max(0, totalCoreLength - result.TotalClassifiedLength);

        // 按钻孔分组统计
        var byBorehole = segmentList.GroupBy(s => s.BoreholeId);
        foreach (var borehole in boreholeList)
        {
            var group = byBorehole.FirstOrDefault(g => g.Key == borehole.Id);
            var classLengths = group != null
                ? group.GroupBy(s => s.RockClass).ToDictionary(g => g.Key, g => g.Sum(s => s.DepthEnd - s.DepthStart))
                : new Dictionary<RockClass, double>();

            double classifiedLength = classLengths.Values.Sum();
            double unclassifiedLength = Math.Max(0, borehole.TotalDepth - classifiedLength);

            var classRatios = classLengths.ToDictionary(
                kv => kv.Key,
                kv => borehole.TotalDepth > 0 ? kv.Value / borehole.TotalDepth : 0);

            var classRatiosByAnalyzed = classLengths.ToDictionary(
                kv => kv.Key,
                kv => classifiedLength > 0 ? kv.Value / classifiedLength : 0);

            result.BoreholeStatistics.Add(new BoreholeStatistics
            {
                BoreholeId = borehole.Id,
                BoreholeNumber = borehole.Number,
                TotalLength = borehole.TotalDepth,
                ClassifiedLength = classifiedLength,
                UnclassifiedLength = unclassifiedLength,
                ClassLengths = classLengths,
                ClassRatios = classRatios,
                ClassRatiosByAnalyzed = classRatiosByAnalyzed,
                ClassSummary = BuildBoreholeClassSummary(classRatios),
                ClassSummaryByAnalyzed = BuildBoreholeClassSummary(classRatiosByAnalyzed)
            });
        }

        // 项目整体统计：每类围岩，按项目岩心总长度计算占比
        var allClassGroups = segmentList.GroupBy(s => s.RockClass);
        double analyzedBase = result.TotalClassifiedLength;
        foreach (RockClass rockClass in Enum.GetValues<RockClass>())
        {
            var group = allClassGroups.FirstOrDefault(g => g.Key == rockClass);
            double totalLength = group?.Sum(s => s.DepthEnd - s.DepthStart) ?? 0;
            int boreholeCount = group?.Select(s => s.BoreholeId).Distinct().Count() ?? 0;

            var ranges = group?.OrderBy(s => s.BoreholeId).ThenBy(s => s.DepthStart)
                .Select(s => new DepthRange
                {
                    Start = s.DepthStart,
                    End = s.DepthEnd
                }).ToList() ?? new List<DepthRange>();

            result.ClassStatistics.Add(new RockClassStatistics
            {
                RockClass = rockClass,
                ClassDescription = ClassDescription(rockClass),
                TotalLength = totalLength,
                Ratio = totalCoreLength > 0 ? totalLength / totalCoreLength : 0,
                RatioByAnalyzed = analyzedBase > 0 ? totalLength / analyzedBase : 0,
                BoreholeCount = boreholeCount,
                DepthRanges = ranges
            });
        }

        // 薄弱段：IV、V 类
        result.WeakSections = segmentList
            .Where(s => s.RockClass is RockClass.IV or RockClass.V)
            .OrderBy(s => s.BoreholeId)
            .ThenBy(s => s.DepthStart)
            .Select(s => new WeakSection
            {
                BoreholeId = s.BoreholeId,
                BoreholeNumber = boreholeDict.TryGetValue(s.BoreholeId, out var b) ? b.Number : $"钻孔{s.BoreholeId}",
                DepthStart = s.DepthStart,
                DepthEnd = s.DepthEnd,
                RockClass = s.RockClass,
                Length = s.DepthEnd - s.DepthStart
            })
            .ToList();

        result.OverallClassSummary = BuildOverallClassSummary(result.ClassStatistics, s => s.Ratio);
        result.OverallClassSummaryByAnalyzed = BuildOverallClassSummary(result.ClassStatistics, s => s.RatioByAnalyzed);
        return result;
    }

    private static string BuildBoreholeClassSummary(Dictionary<RockClass, double> classRatios)
    {
        var parts = classRatios
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{ClassDescription(kv.Key)}类：{kv.Value:P0}")
            .ToList();
        return string.Join("；", parts);
    }

    private static string BuildOverallClassSummary(List<RockClassStatistics> classStatistics, Func<RockClassStatistics, double> ratioSelector)
    {
        var parts = classStatistics
            .OrderBy(s => s.RockClass)
            .Select(s => $"{ClassDescription(s.RockClass)}类：{ratioSelector(s):P0}")
            .ToList();
        return string.Join("；", parts);
    }

    private static string ClassDescription(RockClass rockClass) => RockClassText.Numeral(rockClass);
}

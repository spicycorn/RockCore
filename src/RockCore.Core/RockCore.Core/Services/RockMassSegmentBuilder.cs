namespace RockCore.Core.Services;

/// <summary>
/// 归并输入：一个回次（采样单元）的原始量。
/// 归并只依赖这些原始量，不依赖判级结果——顺序必须是"先归并、后判级"，
/// 否则边界回次的误判会被固化下来。
/// </summary>
public sealed class RockMassRunInput
{
    /// <summary>调用方回填用的标识（Excel 导入用行号）。</summary>
    public int Key { get; init; }

    /// <summary>回次起点（m，含起始深度）。</summary>
    public double DepthStart { get; init; }

    /// <summary>回次终点（m）。</summary>
    public double DepthEnd { get; init; }

    /// <summary>岩心总段数 n = ≥10cm 段数 + 碎屑数。</summary>
    public int TotalPieces { get; init; }

    /// <summary>≥10cm 岩心段累计长度（cm）。</summary>
    public double TotalLengthCm { get; init; }

    /// <summary>F.0.2 岩质键值；0 或负数表示未填（未填时不作为强制分界）。</summary>
    public int RockTypeKey { get; init; }

    /// <summary>回次厚度（m）。</summary>
    public double ThicknessM => DepthEnd - DepthStart;

    /// <summary>回次平均间距（cm）= 厚度×100 ÷ n；n=0 时为 0。</summary>
    public double SpacingCm => TotalPieces > 0 ? ThicknessM * 100 / TotalPieces : 0;
}

/// <summary>
/// 岩体段（完整程度分级单元）：由若干相邻、性质相近的回次归并而成。
/// </summary>
public sealed class RockMassSegment
{
    /// <summary>组内回次（按深度先后）。</summary>
    public List<RockMassRunInput> Runs { get; } = new();

    public double DepthStart { get; private set; }
    public double DepthEnd { get; private set; }

    /// <summary>段厚（m）= 组内各回次进尺之和。</summary>
    public double ThicknessM { get; private set; }

    /// <summary>段内岩心总段数之和 Σn。</summary>
    public int TotalPieces { get; private set; }

    /// <summary>段内 ≥10cm 岩心累计长度之和（cm）。</summary>
    public double TotalLengthCm { get; private set; }

    /// <summary>回次数。</summary>
    public int RunCount => Runs.Count;

    /// <summary>组内是否只有一个回次（未发生归并）。</summary>
    public bool IsSingleRun => Runs.Count == 1;

    /// <summary>
    /// 段的平均间距（cm）= 段厚×100 ÷ Σn。
    /// 用"总长 ÷ 总条数"的合并口径，而不是各回次间距的算术平均——后者会让
    /// 短回次与长回次等权，样本量信息被丢掉。
    /// </summary>
    public double SpacingCm => TotalPieces > 0 ? Math.Round(ThicknessM * 100 / TotalPieces, 2) : 0;

    /// <summary>段的 RQD（%）= Σ合格段长 ÷ (段厚×100) × 100。</summary>
    public double Rqd => ThicknessM > 0 ? Math.Round(TotalLengthCm / (ThicknessM * 100) * 100, 2) : 0;

    /// <summary>组内回次的岩质键值（取第一个已填的；都没填返回 0）。</summary>
    public int RockTypeKey => Runs.FirstOrDefault(r => r.RockTypeKey > 0)?.RockTypeKey ?? 0;

    public void Add(RockMassRunInput run)
    {
        Runs.Add(run);
        Recompute();
    }

    public void InsertAtFront(RockMassRunInput run)
    {
        Runs.Insert(0, run);
        Recompute();
    }

    private void Recompute()
    {
        DepthStart = Runs.Min(r => r.DepthStart);
        DepthEnd = Runs.Max(r => r.DepthEnd);
        ThicknessM = Math.Round(Runs.Sum(r => r.ThicknessM), 4);
        TotalPieces = Runs.Sum(r => r.TotalPieces);
        TotalLengthCm = Math.Round(Runs.Sum(r => r.TotalLengthCm), 2);
    }
}

/// <summary>
/// 回次 → 岩体段归并器。
///
/// 规范里的评价单元是"岩体性质相对均一的连续段"，回次只是钻探取芯的采样单元，
/// 所以判完整程度之前必须先把相邻、指标相近的回次归并成岩体段：
///   · 深度不连续（中间有被跳过/阻断的回次）→ 强制分段；
///   · 岩质已知且不同 → 强制分段；
///   · 平均间距相对差超过容差 → 另起一段；
///   · 归并后仍不足最小段厚的薄段 → 并入间距最接近的相邻段。
/// 归并结果只用到原始量（进尺、段数、段长、岩质），不使用判级结果。
/// </summary>
public static class RockMassSegmentBuilder
{
    /// <summary>深度连续性容差（m），吸收浮点累计误差。</summary>
    private const double DepthEpsilon = 0.0015;

    /// <summary>
    /// 按深度顺序归并回次。
    /// </summary>
    /// <param name="runs">回次集合（顺序不限，内部按起点排序）。</param>
    /// <param name="minThicknessM">岩体段最小厚度（m）；&lt;=0 时不做薄段吸收。</param>
    /// <param name="spacingToleranceRatio">间距相对差容差（0~1）；&gt;=1 时视为不因间距分段。</param>
    public static List<RockMassSegment> Build(
        IReadOnlyList<RockMassRunInput> runs,
        double minThicknessM,
        double spacingToleranceRatio)
    {
        var groups = new List<RockMassSegment>();
        if (runs.Count == 0) return groups;

        foreach (var run in runs.OrderBy(r => r.DepthStart).ThenBy(r => r.DepthEnd))
        {
            if (run.ThicknessM <= 0) continue; // 进尺非正的脏数据不参与归并

            var last = groups.Count > 0 ? groups[^1] : null;
            if (last != null && CanMerge(last, run, spacingToleranceRatio))
                last.Add(run);
            else
            {
                var g = new RockMassSegment();
                g.Add(run);
                groups.Add(g);
            }
        }

        AbsorbThinGroups(groups, minThicknessM);
        return groups;
    }

    /// <summary>
    /// 单个回次自成一段（关闭归并时的退化路径）。
    /// </summary>
    public static List<RockMassSegment> BuildOnePerRun(IReadOnlyList<RockMassRunInput> runs)
    {
        var groups = new List<RockMassSegment>();
        foreach (var run in runs.OrderBy(r => r.DepthStart).ThenBy(r => r.DepthEnd))
        {
            if (run.ThicknessM <= 0) continue;
            var g = new RockMassSegment();
            g.Add(run);
            groups.Add(g);
        }
        return groups;
    }

    private static bool CanMerge(RockMassSegment group, RockMassRunInput run, double tolerance)
    {
        // 强制分界 1：深度不连续（中间有未入库的回次）
        if (!IsContinuous(run.DepthStart, group.DepthEnd)) return false;

        // 强制分界 2：岩质已知且不同
        var groupRock = group.RockTypeKey;
        if (groupRock > 0 && run.RockTypeKey > 0 && groupRock != run.RockTypeKey) return false;

        // 软条件：该回次自身的平均间距与当前段平均间距的相对差须在容差内。
        // 必须用「回次 vs 段」而不是「合并后 vs 段」：后者会让已经很大的段对新的、
        // 性质明显不同的回次几乎不敏感（合并均值被摊薄），实测会把整孔吞成一段。
        if (tolerance >= 1) return true;

        var runSpacing = run.SpacingCm;
        var current = group.SpacingCm;
        var scale = Math.Max(runSpacing, current);
        if (scale <= 0) return true; // 两侧都没有结构面（n=0），不因间距断开

        return Math.Abs(runSpacing - current) <= tolerance * scale;
    }

    /// <summary>
    /// 薄段吸收：把不足最小厚度的段并入"间距最接近且深度连续"的相邻段，循环到没有可吸收的薄段。
    /// 每轮至少减少一个段，必然终止。
    /// </summary>
    private static void AbsorbThinGroups(List<RockMassSegment> groups, double minThicknessM)
    {
        if (minThicknessM <= 0) return;

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g.ThicknessM >= minThicknessM - DepthEpsilon) continue;

                var prev = i > 0 ? groups[i - 1] : null;
                var next = i < groups.Count - 1 ? groups[i + 1] : null;

                var canPrev = prev != null && IsContinuous(g.DepthStart, prev.DepthEnd)
                              && RockCompatible(g, prev);
                var canNext = next != null && IsContinuous(next.DepthStart, g.DepthEnd)
                              && RockCompatible(g, next);
                if (!canPrev && !canNext) continue;

                // 选间距更接近的一侧；差距在容差内时优先并到更厚的一侧（避免薄并薄来回抖动）
                RockMassSegment? target;
                if (canPrev && canNext)
                {
                    var dPrev = Math.Abs(prev!.SpacingCm - g.SpacingCm);
                    var dNext = Math.Abs(next!.SpacingCm - g.SpacingCm);
                    var scale = Math.Max(Math.Max(dPrev, dNext), 1e-9);
                    target = Math.Abs(dPrev - dNext) <= 0.25 * scale
                        ? (prev.ThicknessM >= next.ThicknessM ? prev : next)
                        : (dPrev < dNext ? prev : next);
                }
                else target = canPrev ? prev : next;

                if (ReferenceEquals(target, prev))
                    // 逆序前插，保持组内回次仍按深度先后排列
                    foreach (var run in g.Runs.AsEnumerable().Reverse()) target!.InsertAtFront(run);
                else
                    foreach (var run in g.Runs) target!.Add(run);

                groups.RemoveAt(i);
                changed = true;
                break; // 组索引已变，重新扫描
            }
        }
    }

    /// <summary>深度是否首尾相接（容差吸收浮点累计误差）。</summary>
    private static bool IsContinuous(double start, double end)
        => Math.Abs(start - end) <= DepthEpsilon;

    /// <summary>两个段的岩质是否可归并（任一侧未填时不设限）。</summary>
    private static bool RockCompatible(RockMassSegment a, RockMassSegment b)
    {
        var ra = a.RockTypeKey;
        var rb = b.RockTypeKey;
        return ra <= 0 || rb <= 0 || ra == rb;
    }
}

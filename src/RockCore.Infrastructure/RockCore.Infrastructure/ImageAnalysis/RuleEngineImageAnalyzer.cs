using System.Linq;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Infrastructure.ImageAnalysis;

/// <summary>
/// 岩芯图像分析计算引擎。
/// 输入：标注数据（岩芯段折线、节理位置、比例尺）
/// 输出：完整性分段、结构面列表、分析指标
/// 所有计算均基于真实输入数据，不使用无依据的默认值或假设。
/// 
/// 标注数据来源有两种：
///   1) 人工标注（ManualAnnotationWindow）
///   2) 自动识别（IImageAnalyzer 实现，开发中）
/// 无论来源，后续分析逻辑共用本引擎。
/// </summary>
public class RuleEngineImageAnalyzer
{
    public string Name => "规则引擎分析器";
    public string Version => "1.0.0";

    // ====================================================================
    // 核心入口：基于人工标注数据计算分析结果
    // ====================================================================
    /// <param name="imagePath">原始图像路径</param>
    /// <param name="depthStart">钻孔深度起点 (m)</param>
    /// <param name="depthEnd">钻孔深度终点 (m)</param>
    /// <param name="pixelPerCm">像素/厘米换算系数（从比例尺得出）；<= 0 时视为未提供</param>
    /// <param name="scaleCm">比例尺输入的实际厘米数，同时作为每行岩芯的标准深度长度（cm）</param>
    /// <param name="boxRect">岩芯盒边界 (可选)；无则为 null</param>
    /// <param name="pieces">岩芯段列表 (起点X, 起点Y, 终点X, 终点Y, 折线总长度, 标签, 折线点)</param>
    /// <param name="joints">手动标注的节理线列表 (x1, y1, x2, y2)</param>
    /// <param name="scaleP1">比例尺起点 (可选)；无则为 null</param>
    /// <param name="scaleP2">比例尺终点 (可选)；无则为 null</param>
    /// <param name="imageWidth">原始图像宽度 (像素)，用于训练数据记录</param>
    /// <param name="imageHeight">原始图像高度 (像素)，用于训练数据记录</param>
    public ImageAnalysisResult ComputeFromAnnotation(
        string imagePath,
        double depthStart,
        double depthEnd,
        double pixelPerCm,
        double scaleCm,
        (double x, double y, double w, double h)? boxRect,
        List<(double sx, double sy, double ex, double ey, double len, string label, List<(double x, double y)> points)> pieces,
        List<(double x1, double y1, double x2, double y2)> joints,
        (double x, double y)? scaleP1 = null,
        (double x, double y)? scaleP2 = null,
        int imageWidth = 0,
        int imageHeight = 0)
    {
        // --- 基础参数 ---
        var result = new ImageAnalysisResult
        {
            Success = true,
            ImagePath = imagePath,
            CoreBoxRect = boxRect.HasValue
                ? ((int)boxRect.Value.x, (int)boxRect.Value.y, (int)boxRect.Value.w, (int)boxRect.Value.h)
                : null,
            PixelPerCm = pixelPerCm > 0 ? pixelPerCm : 0
        };

        // --- 转换岩芯段 ---
        var corePieces = pieces.Select((p, idx) => new CorePiece
        {
            StartX = p.sx,
            StartY = p.sy,
            EndX = p.ex,
            EndY = p.ey,
            X = (int)Math.Min(p.sx, p.ex),
            Y = (int)Math.Min(p.sy, p.ey),
            Width = (int)Math.Abs(p.ex - p.sx),
            Height = (int)Math.Abs(p.ey - p.sy),
            YCenter = (p.sy + p.ey) / 2.0,
            LengthCm = pixelPerCm > 0 ? p.len / pixelPerCm : 0,
            Label = string.IsNullOrWhiteSpace(p.label) ? $"P{idx + 1}" : p.label,
            ContourPoints = p.points.Select(pt => new ContourPoint { X = (int)pt.x, Y = (int)pt.y }).ToList()
        }).ToList();

        result.CorePieces = corePieces;

        // --- 转换节理/裂隙 ---
        var jointList = joints.Select(j => new JointInfo
        {
            Line = (j.x1, j.y1, j.x2, j.y2),
            DepthInPhoto = (j.y1 + j.y2) / 2.0,
            Type = "节理",
            WidthPixel = Math.Sqrt((j.x2 - j.x1) * (j.x2 - j.x1) + (j.y2 - j.y1) * (j.y2 - j.y1)),
            WidthCm = pixelPerCm > 0 ? Math.Sqrt((j.x2 - j.x1) * (j.x2 - j.x1) + (j.y2 - j.y1) * (j.y2 - j.y1)) / pixelPerCm : 0,
            LengthPixel = Math.Sqrt((j.x2 - j.x1) * (j.x2 - j.x1) + (j.y2 - j.y1) * (j.y2 - j.y1))
        }).ToList();

        result.Joints = jointList;
        result.JointCount = jointList.Count;

        // --- 节理间距计算：相邻节理在深度方向上的距离 ---
        var jointSpacingCm = new List<double>();
        if (jointList.Count >= 2)
        {
            var sortedJoints = jointList.OrderBy(j => j.DepthInPhoto).ToList();
            for (int i = 1; i < sortedJoints.Count; i++)
            {
                double dyPixel = sortedJoints[i].DepthInPhoto - sortedJoints[i - 1].DepthInPhoto;
                double spacingCm = pixelPerCm > 0 ? dyPixel / pixelPerCm : 0;
                if (spacingCm > 0) jointSpacingCm.Add(spacingCm);
            }
        }

        double maxJointSpacingCm = jointSpacingCm.Count > 0 ? jointSpacingCm.Max() : 0;
        result.AvgJointSpacingCm = jointSpacingCm.Count > 0 ? jointSpacingCm.Average() : 0;

        // --- 识别岩芯行（按绘制顺序）---
        var rows = IdentifyCoreRowsByDrawOrder(corePieces);
        if (rows.Count == 0 && corePieces.Count > 0)
            rows = new List<List<CorePiece>> { corePieces };

        // --- 按深度顺序排列所有 piece（用于最终等级汇总）---
        var depthOrdered = new List<CorePiece>();
        foreach (var row in rows)
        {
            var sortedRow = row.OrderBy(p => Math.Min(p.StartX, p.EndX)).ToList();
            depthOrdered.AddRange(sortedRow);
        }

        // --- 为每个岩芯段计算等级（备用）---
        foreach (var piece in corePieces)
        {
            var (segLevel, segDev, _) = JudgeSegmentDouble(0, piece.LengthCm);
            piece.Level = segLevel;
            piece.Development = segDev;
        }

        // --- 完整性等级与结构面发育程度（两个独立判断维度，按行独立分析后合并）---
        // depthOrdered: 所有 piece 按深度顺序排列（行号+行内X）
        // rows: pieces 按行分组（每行独立累加分析）
        if (depthOrdered.Count > 0)
        {
            result.IntegritySegments = GenerateIntegritySegments(
                rows, depthStart, depthEnd, pixelPerCm, scaleCm);
            result.DevelopmentSegments = GenerateDevelopmentSegments(
                rows, depthStart, depthEnd, pixelPerCm, scaleCm);

            // 反向设置每个 piece 的最终等级（与分段结果一致，用于标注图上色）
            foreach (var seg in result.IntegritySegments)
            {
                if (string.IsNullOrWhiteSpace(seg.PieceLabels)) continue;
                var labels = seg.PieceLabels.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var lbl in labels)
                {
                    var p = corePieces.FirstOrDefault(x => x.Label == lbl);
                    if (p != null)
                    {
                        p.Level = seg.Level;
                        p.Development = seg.Development;
                    }
                }
            }
        }

        // --- 整体等级：从分段结果中取长度占比最大的段的等级 ---
        // 取代废弃的"最大间距+总节理数"判定方法
        if (result.IntegritySegments.Count > 0)
        {
            var dominantSeg = result.IntegritySegments
                .OrderByDescending(s => s.LengthCm)
                .First();
            result.IntegrityLevel = dominantSeg.Level;
        }
        if (result.DevelopmentSegments.Count > 0)
        {
            var dominantDevSeg = result.DevelopmentSegments
                .OrderByDescending(s => s.LengthCm)
                .First();
            result.StructuralDevelopment = dominantDevSeg.Development;
        }

        // --- 结构面 ---
        result.StructuralPlanes = GenerateStructuralPlanes(
            jointList, depthStart, depthEnd, result.CoreBoxRect);

        // --- 标注数据 ---
        result.AnnotationData = BuildAnnotationData(result, scaleP1, scaleP2);

        // --- 训练数据 ---
        result.TrainingData = new ManualAnnotationTrainingData
        {
            ImageWidth = imageWidth,
            ImageHeight = imageHeight,
            DepthStart = depthStart,
            DepthEnd = depthEnd,
            PixelPerCm = pixelPerCm,
            ScalePoint1 = scaleP1,
            ScalePoint2 = scaleP2,
            CorePieces = corePieces.Select((p, i) => new TrainingCorePiece
            {
                Label = p.Label,
                LengthPixel = pieces[i].len,
                LengthCm = p.LengthCm,
                PolylinePoints = p.ContourPoints.Select(c => new PolylinePoint(c.X, c.Y)).ToList()
            }).ToList()
        };

        return result;
    }

    // ====================================================================
    // 解析 piece 标号（"P1" → 1，"P12" → 12）——失败时返回 int.MaxValue
    // 仅用于按深度顺序排列用户标注的各段岩芯。
    // ====================================================================
    private static int ParsePieceNumber(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return int.MaxValue;
        // 取第一个数字序列作为标号
        int start = -1;
        for (int i = 0; i < label.Length; i++)
        {
            if (char.IsDigit(label[i])) { start = i; break; }
        }
        if (start < 0) return int.MaxValue;
        int end = start;
        while (end < label.Length && char.IsDigit(label[end])) end++;
        if (int.TryParse(label.AsSpan(start, end - start), out int n)) return n;
        return int.MaxValue;
    }

    // ====================================================================
    // 识别岩芯行：基于绘制顺序 + X回退 + Y下移 的双层判断
    // 原理：用户按从浅到深顺序绘制，先画第一行（左→右），再画第二行（左→右）
    //       换行时：当前段的左X 大幅小于 前一段的右X（回退到行首），同时 Y 向下移动
    // ====================================================================
    private static List<List<CorePiece>> IdentifyCoreRowsByDrawOrder(List<CorePiece> drawOrderedPieces)
    {
        var rows = new List<List<CorePiece>>();
        if (drawOrderedPieces.Count == 0) return rows;

        var currentRow = new List<CorePiece> { drawOrderedPieces[0] };
        double prevRightX = Math.Max(drawOrderedPieces[0].StartX, drawOrderedPieces[0].EndX);
        double prevYCenter = drawOrderedPieces[0].YCenter;

        for (int i = 1; i < drawOrderedPieces.Count; i++)
        {
            var p = drawOrderedPieces[i];
            double curLeftX = Math.Min(p.StartX, p.EndX);
            double curRightX = Math.Max(p.StartX, p.EndX);
            double curYCenter = p.YCenter;

            // 双层判断：
            //   1) X 坐标回退：当前段的左X < 前一段的右X * 0.7（从行尾回退到行首）
            //      注意：比较的是 当前左X vs 前一个右X，不是 vs 前一个左X
            //      因为用户每行都是从左往右画，两行的左X差不多，但当前左X << 前一个右X
            //   2) Y 坐标下移：当前段的YCenter > 前段的YCenter（向下移动）
            bool xFallback = curLeftX < prevRightX * 0.7;
            bool yMoveDown = curYCenter > prevYCenter + 5.0; // +5px 容差，避免微小波动误判

            if (xFallback && yMoveDown)
            {
                // 换行了
                rows.Add(currentRow);
                currentRow = new List<CorePiece> { p };
            }
            else
            {
                // 同一行
                currentRow.Add(p);
            }

            prevRightX = curRightX;
            prevYCenter = curYCenter;
        }

        rows.Add(currentRow);
        return rows;
    }

    // ====================================================================
    // 识别岩芯行：按 YCenter 聚类分组（旧方法，保留备用）
    // ====================================================================
    private static List<List<CorePiece>> IdentifyCoreRows(List<CorePiece> pieces)
    {
        var rows = new List<List<CorePiece>>();
        if (pieces.Count == 0) return rows;

        var sorted = pieces.OrderBy(p => p.YCenter).ToList();
        double estimatedHeight = sorted.Count > 0
            ? sorted.Average(p => (double)p.Height)
            : 20.0;
        double threshold = Math.Max(40.0, estimatedHeight * 3.0);

        var currentRow = new List<CorePiece> { sorted[0] };
        double currentRowY = sorted[0].YCenter;

        for (int i = 1; i < sorted.Count; i++)
        {
            double dy = Math.Abs(sorted[i].YCenter - currentRowY);
            if (dy <= threshold)
            {
                currentRow.Add(sorted[i]);
                currentRowY = currentRow.Average(p => p.YCenter);
            }
            else
            {
                rows.Add(currentRow);
                currentRow = new List<CorePiece> { sorted[i] };
                currentRowY = sorted[i].YCenter;
            }
        }

        rows.Add(currentRow);
        return rows;
    }

    // ====================================================================
    // 双判定核心方法：严格依据 DL/T 5894-2025 表 F.0.4
    //   输入：jointCount — 节理数（≥ 0）
    //         spacingCm  — 结构面间距（cm），即相邻节理之间的距离
    //   严格匹配：节理数和结构面间距必须同时满足表中某一行的条件
    //   按行从上到下检查（行1最严格，行7最宽松），首次匹配即返回
    //
    //   表 F.0.4（完整边界调为 95cm）：
    //   行1: J∈[1,2] 且 S>95    → 完整 / 不发育
    //   行2: J∈[1,2] 且 S∈(50,95] → 较完整 / 轻度发育
    //   行3: J∈[2,3] 且 S∈(30,50] → 较完整 / 中等发育
    //   行4: J∈[2,3] 且 S∈(10,30] → 完整性差 / 较发育
    //   行5: J∈[2,3] 且 S≤10     → 完整性差 / 发育
    //   行6: J>3     且 S≤10     → 较破碎 / 很发育
    //   行7: S<2cm（无序）       → 破碎 / ——
    //   无匹配时：仅按结构面间距区间兜底（不再考虑节理数组合）
    // ====================================================================
    private static (IntegrityLevel Level, string Development, string Basis)
        JudgeSegmentDouble(int jointCount, double spacingCm)
    {
        // ---- 严格双条件匹配（按表行顺序检查）----
        if (spacingCm > 0 && spacingCm < 2)
        {
            return (IntegrityLevel.Broken, "——", $"结构面间距{spacingCm:F1}cm（<2cm，无序）→ 破碎");
        }
        else if (jointCount >= 1 && jointCount <= 2 && spacingCm > 95)
        {
            return (IntegrityLevel.Intact, "不发育", $"节理{jointCount}条，结构面间距{spacingCm:F1}cm（>95cm）→ 完整/不发育");
        }
        else if (jointCount >= 1 && jointCount <= 2 && spacingCm > 50 && spacingCm <= 95)
        {
            return (IntegrityLevel.RelativelyIntact, "轻度发育", $"节理{jointCount}条，结构面间距{spacingCm:F1}cm（50~95cm）→ 较完整/轻度发育");
        }
        else if (jointCount >= 2 && jointCount <= 3 && spacingCm > 30 && spacingCm <= 50)
        {
            return (IntegrityLevel.RelativelyIntact, "中等发育", $"节理{jointCount}条，结构面间距{spacingCm:F1}cm（30~50cm）→ 较完整/中等发育");
        }
        else if (jointCount >= 2 && jointCount <= 3 && spacingCm > 10 && spacingCm <= 30)
        {
            return (IntegrityLevel.Poor, "较发育", $"节理{jointCount}条，结构面间距{spacingCm:F1}cm（10~30cm）→ 完整性差/较发育");
        }
        else if (jointCount >= 2 && jointCount <= 3 && spacingCm > 0 && spacingCm <= 10)
        {
            return (IntegrityLevel.Poor, "发育", $"节理{jointCount}条，结构面间距{spacingCm:F1}cm（≤10cm）→ 完整性差/发育");
        }
        else if (jointCount > 3 && spacingCm > 0 && spacingCm <= 10)
        {
            return (IntegrityLevel.RelativelyBroken, "很发育", $"节理{jointCount}条（>3），结构面间距{spacingCm:F1}cm（≤10cm）→ 较破碎/很发育");
        }

        // ---- 无严格匹配时：仅按结构面间距区间兜底 ----
        if (spacingCm > 95)
            return (IntegrityLevel.Intact, "不发育", $"结构面间距{spacingCm:F1}cm（>95cm，仅按间距兜底）→ 完整/不发育");
        else if (spacingCm > 50 && spacingCm <= 95)
            return (IntegrityLevel.RelativelyIntact, "轻度发育", $"结构面间距{spacingCm:F1}cm（50~95cm，仅按间距兜底）→ 较完整/轻度发育");
        else if (spacingCm > 30 && spacingCm <= 50)
            return (IntegrityLevel.RelativelyIntact, "中等发育", $"结构面间距{spacingCm:F1}cm（30~50cm，仅按间距兜底）→ 较完整/中等发育");
        else if (spacingCm > 10 && spacingCm <= 30)
            return (IntegrityLevel.Poor, "较发育", $"结构面间距{spacingCm:F1}cm（10~30cm，仅按间距兜底）→ 完整性差/较发育");
        else if (spacingCm > 2 && spacingCm <= 10)
            return (IntegrityLevel.Poor, "发育", $"结构面间距{spacingCm:F1}cm（2~10cm，仅按间距兜底）→ 完整性差/发育");
        else if (spacingCm > 0 && spacingCm <= 2)
            return (IntegrityLevel.Broken, "——", $"结构面间距{spacingCm:F1}cm（≤2cm，无序）→ 破碎");
        else
            return (IntegrityLevel.Poor, "发育", "结构面间距未知（保守判定）");
    }

    // ====================================================================
    // 完整性等级分段（独立判断维度）
    // 三阶段合并：单piece等级初步合并 → 最终判定 → 强行合并相邻同等级段
    // ====================================================================
    private static List<IntegritySegment> GenerateIntegritySegments(
        List<List<CorePiece>> rows, double depthStart, double depthEnd, double pixelPerCm, double scaleCm)
    {
        return GenerateSegments<IntegritySegment>(
            rows, depthStart, depthEnd, pixelPerCm, scaleCm,
            (jointCount, avgSpacingCm) =>
            {
                var (level, _, basis) = JudgeSegmentDouble(jointCount, avgSpacingCm);
                return (level.ToString(), basis);
            },
            (pieces, jointCount, avgSpacingCm, key, basis, ds, de) =>
            {
                var first = pieces[0];
                double anchorX = Math.Min(first.StartX, first.EndX);
                double anchorY = Math.Min(first.StartY, first.EndY);
                return new IntegritySegment
                {
                    DepthStart = ds,
                    DepthEnd = de,
                    Level = ParseIntegrityLevel(key),
                    Development = string.Empty,
                    LengthCm = pieces.Sum(p => p.LengthCm),
                    Confidence = pixelPerCm > 0 ? 0.9 : 0.3,
                    Basis = basis,
                    JointCount = jointCount,
                    ImageLeft = pieces.Min(p => Math.Min(p.StartX, p.EndX)),
                    ImageTop = pieces.Min(p => Math.Min(p.StartY, p.EndY)),
                    ImageRight = pieces.Max(p => Math.Max(p.StartX, p.EndX)),
                    ImageBottom = pieces.Max(p => Math.Max(p.StartY, p.EndY)),
                    ImageCenterX = (pieces.Min(p => Math.Min(p.StartX, p.EndX)) + pieces.Max(p => Math.Max(p.StartX, p.EndX))) / 2.0,
                    ImageCenterY = (pieces.Min(p => Math.Min(p.StartY, p.EndY)) + pieces.Max(p => Math.Max(p.StartY, p.EndY))) / 2.0,
                    LabelAnchorX = anchorX,
                    LabelAnchorY = anchorY,
                    PieceLabels = string.Join(",", pieces.Select(p => p.Label).Where(l => !string.IsNullOrWhiteSpace(l)))
                };
            });
    }

    // ====================================================================
    // 结构面发育程度分段（独立判断维度）
    // 三阶段合并：单piece等级初步合并 → 最终判定 → 强行合并相邻同发育程度段
    // ====================================================================
    private static List<DevelopmentSegment> GenerateDevelopmentSegments(
        List<List<CorePiece>> rows, double depthStart, double depthEnd, double pixelPerCm, double scaleCm)
    {
        return GenerateSegments<DevelopmentSegment>(
            rows, depthStart, depthEnd, pixelPerCm, scaleCm,
            (jointCount, avgSpacingCm) =>
            {
                var (_, dev, basis) = JudgeSegmentDouble(jointCount, avgSpacingCm);
                return (dev, basis);
            },
            (pieces, jointCount, avgSpacingCm, key, basis, ds, de) =>
            {
                var first = pieces[0];
                double anchorX = Math.Min(first.StartX, first.EndX);
                double anchorY = Math.Min(first.StartY, first.EndY);
                return new DevelopmentSegment
                {
                    DepthStart = ds,
                    DepthEnd = de,
                    Development = key,
                    LengthCm = pieces.Sum(p => p.LengthCm),
                    Confidence = pixelPerCm > 0 ? 0.9 : 0.3,
                    Basis = basis,
                    JointCount = jointCount,
                    AvgSpacingCm = avgSpacingCm,
                    ImageLeft = pieces.Min(p => Math.Min(p.StartX, p.EndX)),
                    ImageTop = pieces.Min(p => Math.Min(p.StartY, p.EndY)),
                    ImageRight = pieces.Max(p => Math.Max(p.StartX, p.EndX)),
                    ImageBottom = pieces.Max(p => Math.Max(p.StartY, p.EndY)),
                    ImageCenterX = (pieces.Min(p => Math.Min(p.StartX, p.EndX)) + pieces.Max(p => Math.Max(p.StartX, p.EndX))) / 2.0,
                    ImageCenterY = (pieces.Min(p => Math.Min(p.StartY, p.EndY)) + pieces.Max(p => Math.Max(p.StartY, p.EndY))) / 2.0,
                    LabelAnchorX = anchorX,
                    LabelAnchorY = anchorY,
                    PieceLabels = string.Join(",", pieces.Select(p => p.Label).Where(l => !string.IsNullOrWhiteSpace(l)))
                };
            });
    }

    /// <summary>
    /// 通用分段合并逻辑（三阶段）：
    /// 阶段1：按行遍历，每个piece单独计算等级（J=0），相邻同等级的piece合并为初步分段
    /// 阶段2：对每个初步分段，用 J+S_avg 做最终判定
    /// 阶段3：全局合并——所有行的分段按深度顺序排列，相邻同最终等级的段强行合并
    ///        （合并后用新参数重新判定，直接接受结果）
    /// 注：行与行之间没有节理（岩芯右侧不计为结构面），合并后的节理数 = 两段节理数之和
    /// 深度分配规则：
    ///   - 前 N-1 行：每行深度 = scaleCm/100 米（标准行长度）
    ///   - 最后一行：按该行岩芯实际长度cm/100 计算深度
    ///   - 每行内的分段按长度比例分配该行的深度
    /// </summary>
    private static List<T> GenerateSegments<T>(
        List<List<CorePiece>> rows,
        double depthStart,
        double depthEnd,
        double pixelPerCm,
        double scaleCm,
        Func<int, double, (string Key, string Basis)> classify,
        Func<List<CorePiece>, int, double, string, string, double, double, T> factory)
    {
        var result = new List<T>();
        if (rows.Count == 0) return result;

        bool hasScale = pixelPerCm > 0 && scaleCm > 0;
        int rowCount = rows.Count;

        // --- 计算每行的深度范围 ---
        var rowDepthStarts = new double[rowCount];
        var rowDepthEnds = new double[rowCount];
        double depthCursor = depthStart;

        for (int r = 0; r < rowCount; r++)
        {
            double rowDepthLen;
            if (r < rowCount - 1)
            {
                // 前 N-1 行：固定深度 = scaleCm / 100 米
                rowDepthLen = hasScale ? scaleCm / 100.0 : (depthEnd - depthStart) / Math.Max(1, rowCount);
            }
            else
            {
                // 最后一行：按该行岩芯实际长度计算
                if (hasScale)
                {
                    double rowLenCm = rows[r].Sum(p => p.LengthCm);
                    rowDepthLen = rowLenCm / 100.0;
                }
                else
                {
                    rowDepthLen = (depthEnd - depthStart) / Math.Max(1, rowCount);
                }
            }

            rowDepthStarts[r] = depthCursor;
            rowDepthEnds[r] = Math.Min(depthCursor + rowDepthLen, depthEnd);
            depthCursor = rowDepthEnds[r];
        }

        // 收集所有行的初步判定结果（已分配行内深度）
        var allSegments = new List<(List<CorePiece> Pieces, int JointCount, double AvgS, string Key, string Basis, double DepthStart, double DepthEnd)>();

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Count == 0) continue;

            // ---- 阶段1：单piece等级，相邻同等级合并 ----
            var prelim = new List<(List<CorePiece> Pieces, string PrelimKey)>();
            var currentPieces = new List<CorePiece> { row[0] };
            string currentKey = ComputeSinglePieceKey(row[0], hasScale, classify);

            for (int i = 1; i < row.Count; i++)
            {
                string nextKey = ComputeSinglePieceKey(row[i], hasScale, classify);
                if (nextKey == currentKey)
                {
                    currentPieces.Add(row[i]);
                }
                else
                {
                    prelim.Add((new List<CorePiece>(currentPieces), currentKey));
                    currentPieces = new List<CorePiece> { row[i] };
                    currentKey = nextKey;
                }
            }
            prelim.Add((new List<CorePiece>(currentPieces), currentKey));

            // ---- 阶段2：最终判定（J + S_avg）----
            var rowSegs = new List<(List<CorePiece> Pieces, int JointCount, double AvgS, string Key, string Basis)>();
            foreach (var ps in prelim)
            {
                int jc = ps.Pieces.Count - 1;
                double avgS = hasScale && ps.Pieces.Count > 0 ? ps.Pieces.Average(p => p.LengthCm) : 0;
                var (key, basis) = hasScale && avgS > 0
                    ? classify(jc, avgS)
                    : ("未知", "缺少比例尺");
                rowSegs.Add((ps.Pieces, jc, avgS, key, basis));
            }

            // ---- 按行内长度比例分配该行的深度 ----
            double rStart = rowDepthStarts[r];
            double rEnd = rowDepthEnds[r];
            double rDepth = rEnd - rStart;
            double rowLenCm = hasScale ? row.Sum(p => p.LengthCm) : 0;

            double rowDepthCursor = rStart;
            foreach (var seg in rowSegs)
            {
                double segLenCm = seg.Pieces.Sum(p => p.LengthCm);
                double segDepthLen;
                if (hasScale && rowLenCm > 0)
                {
                    segDepthLen = segLenCm / rowLenCm * rDepth;
                }
                else
                {
                    segDepthLen = rDepth / Math.Max(1, rowSegs.Count);
                }
                double segDepthEnd = Math.Min(rowDepthCursor + segDepthLen, rEnd);

                allSegments.Add((seg.Pieces, seg.JointCount, seg.AvgS, seg.Key, seg.Basis, rowDepthCursor, segDepthEnd));
                rowDepthCursor = segDepthEnd;
            }
        }

        // ---- 阶段3：全局强行合并（相邻同等级就合并，跨行也合并）----
        // 强行合并：等级相同就合并，合并后等级保持不变（不重新计算），仅累加长度和节理数
        // 深度范围也合并（取并集）
        var merged = new List<(List<CorePiece> Pieces, int JointCount, double AvgS, string Key, string Basis, double DepthStart, double DepthEnd)>();
        foreach (var seg in allSegments)
        {
            if (merged.Count > 0 && merged[^1].Key == seg.Key)
            {
                var last = merged[^1];
                var mergedPieces = last.Pieces.Concat(seg.Pieces).ToList();
                int newJc = last.JointCount + seg.JointCount;
                double newAvgS = hasScale && mergedPieces.Count > 0 ? mergedPieces.Average(p => p.LengthCm) : 0;
                string mergedKey = last.Key;
                string mergedBasis = $"相邻同{mergedKey}段合并（共{mergedPieces.Count}段岩芯，节理{newJc}条，平均间距{(newAvgS > 0 ? $"{newAvgS:F1}cm" : "未知")}）";
                double mergedDepthStart = last.DepthStart;
                double mergedDepthEnd = seg.DepthEnd;
                merged[^1] = (mergedPieces, newJc, newAvgS, mergedKey, mergedBasis, mergedDepthStart, mergedDepthEnd);
            }
            else
            {
                merged.Add(seg);
            }
        }

        // ---- 生成结果对象 ----
        foreach (var seg in merged)
        {
            result.Add(factory(seg.Pieces, seg.JointCount, seg.AvgS, seg.Key, seg.Basis, seg.DepthStart, seg.DepthEnd));
        }

        return result;
    }

    /// <summary>
    /// 单 piece 的等级（J=0，只看自身长度）
    /// 用于阶段1的初步合并依据
    /// </summary>
    private static string ComputeSinglePieceKey(
        CorePiece p,
        bool hasScale,
        Func<int, double, (string Key, string Basis)> classify)
    {
        if (!hasScale || p.LengthCm <= 0) return "未知";
        var (key, _) = classify(0, p.LengthCm);
        return key;
    }

    private static IntegrityLevel ParseIntegrityLevel(string enumName)
    {
        return enumName switch
        {
            "Intact" => IntegrityLevel.Intact,
            "RelativelyIntact" => IntegrityLevel.RelativelyIntact,
            "Poor" => IntegrityLevel.Poor,
            "RelativelyBroken" => IntegrityLevel.RelativelyBroken,
            "Broken" => IntegrityLevel.Broken,
            "UserOverride" => IntegrityLevel.UserOverride,
            _ => IntegrityLevel.Unknown
        };
    }

    // ====================================================================
    // 结构面列表：仅保存真实坐标和宽度，禁止使用 Strike=0/Dip=0/Depth=0 等虚假工程参数
    // ====================================================================
    private static List<StructuralPlane> GenerateStructuralPlanes(
        List<JointInfo> joints, double depthStart, double depthEnd,
        (int X, int Y, int Width, int Height)? boxRect)
    {
        var planes = new List<StructuralPlane>();

        foreach (var j in joints)
        {
            planes.Add(new StructuralPlane
            {
                PlaneType = j.Type,
                Strike = null,           // 单张照片无法测量倾向 → 留空，不伪造 0
                Dip = null,              // 单张照片无法测量倾角 → 留空，不伪造 0
                DepthInPhoto = null,     // 照片内局部深度无工程意义 → 留空
                GlobalDepth = null,      // 全局钻孔深度需要外部计算 → 留空
                ApertureWidthCm = j.WidthCm,
                Roughness = string.Empty, // 未识别：留空字符串，不伪造文本
                ImageX = (j.Line.X1 + j.Line.X2) / 2,
                ImageY = (j.Line.Y1 + j.Line.Y2) / 2
            });
        }

        return planes;
    }

    // ====================================================================
    // 标注数据：用于在原图上绘制分析结果（等级、发育程度、RQD等）
    // ====================================================================
    private static ImageAnnotationData BuildAnnotationData(
        ImageAnalysisResult result,
        (double x, double y)? scaleP1,
        (double x, double y)? scaleP2)
    {
        var data = new ImageAnnotationData
        {
            PixelPerCm = result.PixelPerCm,
            CoreBoxRect = result.CoreBoxRect.HasValue
                ? AnnotationRect.FromXYWH(result.CoreBoxRect.Value.X, result.CoreBoxRect.Value.Y, result.CoreBoxRect.Value.Width, result.CoreBoxRect.Value.Height)
                : null
        };

        if (scaleP1.HasValue && scaleP2.HasValue)
        {
            double minX = Math.Min(scaleP1.Value.x, scaleP2.Value.x);
            double minY = Math.Min(scaleP1.Value.y, scaleP2.Value.y);
            double maxX = Math.Max(scaleP1.Value.x, scaleP2.Value.x);
            double maxY = Math.Max(scaleP1.Value.y, scaleP2.Value.y);
            data.RulerRect = AnnotationRect.FromXYWH(minX, minY, maxX - minX, maxY - minY);
        }

        foreach (var p in result.CorePieces)
        {
            double minPx = p.ContourPoints.Count > 0 ? p.ContourPoints.Min(c => c.X) : Math.Min(p.StartX, p.EndX);
            double minPy = p.ContourPoints.Count > 0 ? p.ContourPoints.Min(c => c.Y) : Math.Min(p.StartY, p.EndY);
            double maxPx = p.ContourPoints.Count > 0 ? p.ContourPoints.Max(c => c.X) : Math.Max(p.StartX, p.EndX);
            double maxPy = p.ContourPoints.Count > 0 ? p.ContourPoints.Max(c => c.Y) : Math.Max(p.StartY, p.EndY);

            data.CorePieces.Add(new CorePieceAnnotation
            {
                Rect = AnnotationRect.FromXYWH(minPx, minPy, maxPx - minPx, maxPy - minPy),
                LengthCm = p.LengthCm,
                Level = p.Level,
                Development = p.Development,
                ContourPoints = p.ContourPoints.ToList()
            });
        }

        foreach (var j in result.Joints)
        {
            data.Joints.Add(new JointAnnotation
            {
                Start = AnnotationPoint.FromXY(j.Line.X1, j.Line.Y1),
                End = AnnotationPoint.FromXY(j.Line.X2, j.Line.Y2),
                Type = j.Type,
                WidthCm = j.WidthCm,
                Angle = j.Angle,
                IsThrough = j.IsThrough,
                ContourPoints = j.ContourPoints.ToList()
            });
        }

        return data;
    }
}

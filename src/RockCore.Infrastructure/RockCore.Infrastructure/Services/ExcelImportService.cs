using System.ComponentModel;
using System.Reflection;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Core.Services;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Services;

public enum ImportRowStatus { Ok, Warning, Blocked }

/// <summary>
/// Excel 导入预览的单行结果（一行 = 一个回次）。
/// 全部派生指标由软件按原始数据重算，不信任 Excel 公式列。
/// </summary>
public class ExcelImportRow
{
    public int RowNumber { get; set; }
    public int? RunNo { get; set; }
    public double? RunLengthM { get; set; }
    public int? FragCount { get; set; }
    public List<double> Pieces { get; } = new();

    public int PieceCount => Pieces.Count;
    public double TotalLengthCm => Math.Round(Pieces.Sum(), 1);
    public double? Rqd { get; set; }
    public double DepthStart { get; set; }
    public double DepthEnd { get; set; }
    public int TotalPieces { get; set; }
    public int JointCount { get; set; }
    public double AvgSpacingCm { get; set; }
    public IntegrityLevel Level { get; set; } = IntegrityLevel.Unknown;

    public RockType RockType { get; set; } = RockType.NotSet;
    public RockStructureType RockStructure { get; set; } = RockStructureType.NotSet;
    public RockHardnessLevel Hardness { get; set; } = RockHardnessLevel.NotSet;
    public RockHomogeneity Homogeneity { get; set; } = RockHomogeneity.NotSet;
    public GroundwaterCondition Groundwater { get; set; } = GroundwaterCondition.NotSet;
    public bool? CaveAxisAngleLessThan30 { get; set; }
    public bool F02Complete { get; set; }

    public ImportRowStatus Status { get; set; } = ImportRowStatus.Ok;
    public List<string> Messages { get; } = new();

    /// <summary>归入的完整性分段编号（1 起；0 = 未归入，如被阻断行）。</summary>
    public int SegmentNo { get; set; }

    // ===== 预览界面显示用 =====
    public string RunNoText => RunNo?.ToString() ?? "—";
    public string RunLengthText => RunLengthM?.ToString("0.00") ?? "—";
    public string FragText => FragCount?.ToString() ?? "—";
    public string RqdText => Rqd?.ToString("0.00") ?? "—";
    public string DepthText => $"{DepthStart:0.00} → {DepthEnd:0.00}";
    public string SpacingText => AvgSpacingCm > 0 ? AvgSpacingCm.ToString("0.00") : "—";
    public string LevelText => Level.GetDescription();
    public string SegmentText => SegmentNo > 0 ? SegmentNo.ToString() : "—";
    public string StatusText => Status switch
    {
        ImportRowStatus.Ok => "正常",
        ImportRowStatus.Warning => "警告",
        _ => "阻断"
    };
    public string MessageText => string.Join("；", Messages);
}

/// <summary>
/// 完整性分段（岩体段）：由相邻、平均间距相近的回次归并而成，是规范意义上的评价单元。
/// 指标为组内合并口径：S = 段厚×100÷Σn、RQD = Σ合格段长÷(段厚×100)×100。
/// </summary>
public class ExcelImportSegment
{
    public int No { get; set; }
    public double DepthStart { get; set; }
    public double DepthEnd { get; set; }
    public double ThicknessM { get; set; }
    public int RunCount { get; set; }
    public int TotalPieces { get; set; }
    public double SpacingCm { get; set; }
    public double Rqd { get; set; }
    public IntegrityLevel Level { get; set; } = IntegrityLevel.Unknown;

    /// <summary>F.0.2 六列取自组内填写最完整的那个回次。</summary>
    public ExcelImportRow? F02Source { get; set; }

    public string DepthText => $"{DepthStart:0.00} → {DepthEnd:0.00}";
    public string ThicknessText => ThicknessM.ToString("0.00");
    public string SpacingText => SpacingCm > 0 ? SpacingCm.ToString("0.00") : "—";
    public string RqdText => Rqd.ToString("0.00");
    public string LevelText => Level.GetDescription();
    public string F02Text => F02Source == null || !F02Source.F02Complete
        ? "未填全（导入后可在分段中补填）"
        : "已填全";
}

public class ExcelImportPreview
{
    public string FilePath { get; set; } = string.Empty;
    public string? Error { get; set; }
    public List<ExcelImportRow> Rows { get; } = new();

    /// <summary>归并后的完整性分段（= 实际写入 BoreholeIntegritySegments 的内容）。</summary>
    public List<ExcelImportSegment> Segments { get; } = new();

    /// <summary>归并口径说明，直接展示在导入对话框底部。</summary>
    public string SegmentNote { get; set; } = string.Empty;

    public int OkCount => Rows.Count(r => r.Status == ImportRowStatus.Ok);
    public int WarnCount => Rows.Count(r => r.Status == ImportRowStatus.Warning);
    public int BlockCount => Rows.Count(r => r.Status == ImportRowStatus.Blocked);
}

public class ExcelImportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ImportedCount { get; set; }
    public int ReplacedBatchCount { get; set; }
}

/// <summary>
/// 岩心回次统计 Excel 导入服务（模板 v4：一钻孔一文件，「数据录入」+「计算成果」双表）。
///
/// 数据链（三层，顺序不可颠倒）：
///   回次（采样单元，一行一个回次）
///     → 岩体段（评价单元：相邻、平均间距相近的回次归并，见 <see cref="RockMassSegmentBuilder"/>）
///       → 完整性分段（对归并段判级后写入 BoreholeIntegritySegments）
///
/// 铁律：
///   · 全部派生指标由原始数据重算，不信任 Excel 公式列；
///     n = ≥10cm段数 + 碎屑数；S = 进尺×100÷n (cm)；RQD = ΣL÷(进尺×100)×100；
///   · 判级只按平均间距 S 查表 F.0.4——「结构面发育组数」是地质描述量，回次取芯算不出它，
///     旧实现把"回次内节理条数 n−1"当组数代入，量纲错误导致判定表形同虚设；
///   · 先归并、后判级：归并只用原始量（进尺/段数/段长/岩质），不用判级结果。
/// </summary>
public class ExcelImportService
{
    private const string InputSheetName = "数据录入";
    private const string ResultSheetName = "计算成果";
    private const int MaxPieceColumns = 30;   // L1..L30

    private readonly RockCoreDbContext _context;
    private readonly ISpecificationService _specificationService;

    public ExcelImportService(RockCoreDbContext context, ISpecificationService specificationService)
    {
        _context = context;
        _specificationService = specificationService;
    }

    // ====================================================================
    // 预览：解析 + 重算 + 校验 + 判级（不落库）
    // ====================================================================
    public ExcelImportPreview Preview(string filePath)
    {
        var preview = new ExcelImportPreview { FilePath = filePath };

        if (!File.Exists(filePath))
        {
            preview.Error = "文件不存在";
            return preview;
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(filePath);
        }
        catch (Exception ex)
        {
            preview.Error = $"无法打开 Excel 文件：{ex.Message}";
            return preview;
        }

        using (workbook)
        {
            var wsIn = workbook.Worksheets.FirstOrDefault(
                ws => ws.Name.Trim() == InputSheetName);
            if (wsIn == null)
            {
                preview.Error = $"未找到「{InputSheetName}」工作表，请使用模板文件填写";
                return preview;
            }
            var wsOut = workbook.Worksheets.FirstOrDefault(
                ws => ws.Name.Trim() == ResultSheetName);

            double depthCursor = 0;
            var lastRow = wsIn.LastRowUsed() is { } lastUsed ? lastUsed.RowNumber() : 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var runLen = ReadDouble(wsIn.Cell(r, 2));
                var frags = ReadInt(wsIn.Cell(r, 3));
                var pieces = new List<double>();
                for (int c = 4; c <= 3 + MaxPieceColumns; c++)
                {
                    var v = ReadDouble(wsIn.Cell(r, c));
                    if (v.HasValue) pieces.Add(v.Value);
                }

                // 整行空白 → 跳过；仅空白但有段长/进尺的异常行由校验捕获
                if (runLen == null && frags == null && pieces.Count == 0)
                    continue;

                var row = new ExcelImportRow
                {
                    RowNumber = r,
                    RunNo = ReadInt(wsIn.Cell(r, 1)),
                    RunLengthM = runLen,
                    FragCount = frags,
                };
                row.Pieces.AddRange(pieces);

                // ===== 校验 =====
                if (runLen == null)
                    row.Messages.Add("缺少回次进尺");
                else if (runLen.Value <= 0)
                    row.Messages.Add("回次进尺必须大于 0");
                if (pieces.Any(p => p < 10))
                    row.Messages.Add("存在 <10cm 的段长（明细只登记 ≥10cm 段）");
                if (runLen is > 0 && row.TotalLengthCm > runLen.Value * 100 + 0.05)
                    row.Messages.Add($"段长合计 {row.TotalLengthCm:0.#}cm 超过进尺 {runLen.Value * 100:0.#}cm");

                int n = row.PieceCount + (frags ?? 0);
                if (runLen is > 0 && n == 0)
                    row.Messages.Add("无 ≥10cm 段也无碎屑计数，无法判级");

                if (row.Messages.Count > 0)
                    row.Status = ImportRowStatus.Blocked;

                // ===== 警告 =====
                if (frags == null && row.Status != ImportRowStatus.Blocked)
                {
                    row.Messages.Add("碎屑数空缺，按 0 处理");
                    row.Status = ImportRowStatus.Warning;
                }
                if (runLen is > 0 && frags > 0 && row.Status != ImportRowStatus.Blocked)
                {
                    double fragAvg = (runLen.Value * 100 - row.TotalLengthCm) / frags.Value;
                    if (fragAvg >= 10)
                    {
                        row.Messages.Add($"碎屑平均长约 {fragAvg:0.#}cm ≥10cm，碎屑计数与量测矛盾");
                        row.Status = ImportRowStatus.Warning;
                    }
                }

                // ===== 重算派生指标（深度按行序累计，含被排除行以保持物理连续）=====
                if (runLen is > 0)
                {
                    row.DepthStart = Math.Round(depthCursor, 3);
                    row.DepthEnd = Math.Round(depthCursor + runLen.Value, 3);
                    depthCursor += runLen.Value;

                    // 小数位与模板「计算成果」表一致：RQD、平均间距均保留 2 位
                    row.Rqd = row.PieceCount > 0
                        ? Math.Round(row.TotalLengthCm / (runLen.Value * 100) * 100, 2)
                        : 0;
                    row.TotalPieces = n;
                    row.JointCount = Math.Max(0, n - 1);
                    row.AvgSpacingCm = n > 0 ? Math.Round(runLen.Value * 100 / n, 2) : 0;

                    if (row.AvgSpacingCm > 500 && row.Status != ImportRowStatus.Blocked)
                    {
                        row.Messages.Add("平均间距 >500cm，疑似段长按 mm 误填");
                        row.Status = ImportRowStatus.Warning;
                    }
                    row.Level = Judge(row.AvgSpacingCm);
                }

                // ===== F.0.2 六列（计算成果表，同行对齐）=====
                if (wsOut != null)
                    ReadF02(wsOut, r, row);

                preview.Rows.Add(row);
            }

            if (preview.Rows.Count == 0)
            {
                preview.Error = $"「{InputSheetName}」中没有数据行";
                return preview;
            }

            // 归并成完整性分段（预览默认锚点 0；导入时会按实际锚点重算）
            RebuildSegments(preview,
                preview.Rows.Where(r => r.Status != ImportRowStatus.Blocked).ToList());
            return preview;
        }
    }

    // ====================================================================
    // 导入：同钻孔旧导入批次整体替换，写入批次 + 回次统计 + 完整性分段
    // ====================================================================
    public async Task<ExcelImportResult> ImportAsync(
        string filePath, int boreholeId, double startDepth, bool onlyOkRows)
    {
        var preview = Preview(filePath);
        if (preview.Error != null)
            return new ExcelImportResult { Message = preview.Error };

        var importRows = preview.Rows
            .Where(r => r.Status != ImportRowStatus.Blocked && r.RunLengthM > 0)
            .Where(r => !onlyOkRows || r.Status == ImportRowStatus.Ok)
            .ToList();
        if (importRows.Count == 0)
            return new ExcelImportResult { Message = "没有可导入的行（全部被阻断或已排除）" };

        // 深度从起始深度重新累计（预览默认 0，导入对话框可设锚点）
        double cursor = startDepth;
        foreach (var row in preview.Rows.Where(r => r.RunLengthM > 0))
        {
            row.DepthStart = Math.Round(cursor, 3);
            row.DepthEnd = Math.Round(cursor + row.RunLengthM!.Value, 3);
            cursor += row.RunLengthM.Value;
        }

        // 锚点变了，按最终深度与实际入库的行集重算归并结果
        RebuildSegments(preview, importRows);
        if (preview.Segments.Count == 0)
            return new ExcelImportResult { Message = "没有可导入的回次（进尺均为 0）" };

        var connection = _context.GetConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. 删除该钻孔此前的 Excel 导入分段 + 批次（RunStatistics 级联删除）
            int replaced;
            using (var cnt = connection.CreateCommand())
            {
                cnt.Transaction = transaction;
                cnt.CommandText = @"
                    SELECT COUNT(DISTINCT ImportBatchId) FROM BoreholeIntegritySegments
                    WHERE BoreholeId = @b AND ImportBatchId IS NOT NULL;";
                cnt.Parameters.AddWithValue("@b", boreholeId);
                replaced = Convert.ToInt32(await cnt.ExecuteScalarAsync());
            }
            using (var del = connection.CreateCommand())
            {
                del.Transaction = transaction;
                del.CommandText = "DELETE FROM BoreholeIntegritySegments WHERE BoreholeId = @b AND ImportBatchId IS NOT NULL;";
                del.Parameters.AddWithValue("@b", boreholeId);
                await del.ExecuteNonQueryAsync();
            }
            using (var del = connection.CreateCommand())
            {
                del.Transaction = transaction;
                del.CommandText = "DELETE FROM ImportBatches WHERE BoreholeId = @b;";
                del.Parameters.AddWithValue("@b", boreholeId);
                await del.ExecuteNonQueryAsync();
            }

            // 2. 新批次
            int batchId;
            using (var ins = connection.CreateCommand())
            {
                ins.Transaction = transaction;
                ins.CommandText = @"
                    INSERT INTO ImportBatches (BoreholeId, FileName, SourceType, RowCount, Note, ImportedAt)
                    VALUES (@b, @f, 'Excel', @n, @note, @t);
                    SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("@b", boreholeId);
                ins.Parameters.AddWithValue("@f", Path.GetFileName(filePath));
                ins.Parameters.AddWithValue("@n", importRows.Count);
                int skipped = preview.Rows.Count(r => r.Status == ImportRowStatus.Warning) -
                              importRows.Count(r => r.Status == ImportRowStatus.Warning);
                ins.Parameters.AddWithValue("@note", skipped > 0 ? $"跳过 {skipped} 个警告行" : DBNull.Value);
                ins.Parameters.AddWithValue("@t", DateTime.Now.ToString("o"));
                batchId = Convert.ToInt32(await ins.ExecuteScalarAsync());
            }

            // 3. 回次统计：一个回次一条，保留原始采样记录（可溯源）
            var now = DateTime.Now;
            foreach (var row in importRows)
            {
                using (var ins = connection.CreateCommand())
                {
                    ins.Transaction = transaction;
                    ins.CommandText = @"
                        INSERT INTO RunStatistics
                            (BoreholeId, ImportBatchId, RowNumber, RunNo, RunLengthM, FragCount,
                             PieceCount, TotalLengthCm, Rqd, DepthStart, DepthEnd,
                             JointCount, AvgSpacingCm, IntegrityLevel, CreatedAt)
                        VALUES
                            (@b, @batch, @row, @runNo, @len, @frags,
                             @pc, @sum, @rqd, @ds, @de,
                             @j, @s, @lvl, @t);";
                    ins.Parameters.AddWithValue("@b", boreholeId);
                    ins.Parameters.AddWithValue("@batch", batchId);
                    ins.Parameters.AddWithValue("@row", row.RowNumber);
                    ins.Parameters.AddWithValue("@runNo", (object?)row.RunNo ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@len", row.RunLengthM!.Value);
                    ins.Parameters.AddWithValue("@frags", row.FragCount ?? 0);
                    ins.Parameters.AddWithValue("@pc", row.PieceCount);
                    ins.Parameters.AddWithValue("@sum", row.TotalLengthCm);
                    ins.Parameters.AddWithValue("@rqd", (object?)row.Rqd ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@ds", row.DepthStart);
                    ins.Parameters.AddWithValue("@de", row.DepthEnd);
                    ins.Parameters.AddWithValue("@j", row.JointCount);
                    ins.Parameters.AddWithValue("@s", row.AvgSpacingCm);
                    ins.Parameters.AddWithValue("@lvl", (int)row.Level);
                    ins.Parameters.AddWithValue("@t", now.ToString("o"));
                    await ins.ExecuteNonQueryAsync();
                }
            }

            // 4. 完整性分段：一个岩体段一条（归并后的评价单元，判级在此层进行）
            foreach (var seg in preview.Segments)
            {
                var f02 = seg.F02Source;
                using (var ins = connection.CreateCommand())
                {
                    ins.Transaction = transaction;
                    ins.CommandText = @"
                        INSERT INTO BoreholeIntegritySegments
                            (BoreholeId, DepthStart, DepthEnd, IntegrityLevel, RockType, RockStructureType,
                             RockHardnessLevel, RockHomogeneity, GroundwaterCondition,
                             CaveAxisAngleLessThan30, ImportBatchId, CreatedAt, UpdatedAt)
                        VALUES
                            (@b, @ds, @de, @lvl, @rt, @rs,
                             @rh, @hom, @gw,
                             @cave, @batch, @t, @t2);";
                    ins.Parameters.AddWithValue("@b", boreholeId);
                    ins.Parameters.AddWithValue("@ds", seg.DepthStart);
                    ins.Parameters.AddWithValue("@de", seg.DepthEnd);
                    ins.Parameters.AddWithValue("@lvl", (int)seg.Level);
                    ins.Parameters.AddWithValue("@rt", (int)(f02?.RockType ?? RockType.NotSet));
                    ins.Parameters.AddWithValue("@rs", (int)(f02?.RockStructure ?? RockStructureType.NotSet));
                    ins.Parameters.AddWithValue("@rh", (int)(f02?.Hardness ?? RockHardnessLevel.NotSet));
                    ins.Parameters.AddWithValue("@hom", (int)(f02?.Homogeneity ?? RockHomogeneity.NotSet));
                    ins.Parameters.AddWithValue("@gw", (int)(f02?.Groundwater ?? GroundwaterCondition.NotSet));
                    ins.Parameters.AddWithValue("@cave",
                        f02 != null && f02.CaveAxisAngleLessThan30.HasValue
                            ? (f02.CaveAxisAngleLessThan30.Value ? 1 : 0) : DBNull.Value);
                    ins.Parameters.AddWithValue("@batch", batchId);
                    ins.Parameters.AddWithValue("@t", now.ToString("o"));
                    ins.Parameters.AddWithValue("@t2", now.ToString("o"));
                    await ins.ExecuteNonQueryAsync();
                }
            }

            transaction.Commit();
            var mergedNote = preview.Segments.Count < importRows.Count
                ? $"，归并为 {preview.Segments.Count} 个完整性分段"
                : string.Empty;
            return new ExcelImportResult
            {
                Success = true,
                ImportedCount = importRows.Count,
                ReplacedBatchCount = replaced,
                Message = replaced > 0
                    ? $"已导入 {importRows.Count} 个回次{mergedNote}，并替换此前的 {replaced} 个导入批次"
                    : $"已导入 {importRows.Count} 个回次{mergedNote}"
            };
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // ====================================================================
    // 数据源管理
    // ====================================================================
    public async Task<List<ImportBatch>> GetBatchesAsync(int boreholeId)
    {
        var list = new List<ImportBatch>();
        var connection = _context.GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, BoreholeId, FileName, SourceType, RowCount, Note, ImportedAt
            FROM ImportBatches WHERE BoreholeId = @b ORDER BY ImportedAt DESC";
        cmd.Parameters.AddWithValue("@b", boreholeId);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ImportBatch
            {
                Id = reader.GetInt32(0),
                BoreholeId = reader.GetInt32(1),
                FileName = reader.GetString(2),
                SourceType = reader.GetString(3),
                RowCount = reader.GetInt32(4),
                Note = reader.IsDBNull(5) ? null : reader.GetString(5),
                ImportedAt = DateTime.Parse(reader.GetString(6))
            });
        }
        return list;
    }

    public async Task DeleteBatchAsync(int batchId)
    {
        var connection = _context.GetConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM BoreholeIntegritySegments WHERE ImportBatchId = @id;
            DELETE FROM ImportBatches WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", batchId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ====================================================================
    // 归并：回次（采样单元）→ 完整性分段（评价单元）
    // ====================================================================

    /// <summary>
    /// 把参与入库的回次归并成完整性分段，并回填每行的段号。
    /// 归并只在"将入库的行"之间进行：被排除/阻断的行留下深度空洞，天然成为分界。
    /// 预览与导入各调用一次（导入时深度锚点可能已改，需按新深度重算）。
    /// </summary>
    private void RebuildSegments(ExcelImportPreview preview, IReadOnlyList<ExcelImportRow> groupable)
    {
        preview.Segments.Clear();
        foreach (var row in preview.Rows) row.SegmentNo = 0;

        var thresholds = _specificationService?.CurrentConfig?.ConfigThresholds
                         ?? new RockSpecificationConfig.Thresholds();

        var sources = groupable.Where(r => r.RunLengthM > 0).ToList();
        var byKey = sources.ToDictionary(r => r.RowNumber);
        var inputs = sources.Select(r => new RockMassRunInput
        {
            Key = r.RowNumber,
            DepthStart = r.DepthStart,
            DepthEnd = r.DepthEnd,
            TotalPieces = r.TotalPieces,
            TotalLengthCm = r.TotalLengthCm,
            RockTypeKey = (int)r.RockType,
        }).ToList();

        var built = thresholds.MergeRunsIntoRockSegments
            ? RockMassSegmentBuilder.Build(
                inputs, thresholds.RockSegmentMinThicknessM, thresholds.RockSegmentSpacingToleranceRatio)
            : RockMassSegmentBuilder.BuildOnePerRun(inputs);

        var no = 0;
        foreach (var seg in built)
        {
            no++;
            var model = new ExcelImportSegment
            {
                No = no,
                DepthStart = seg.DepthStart,
                DepthEnd = seg.DepthEnd,
                ThicknessM = seg.ThicknessM,
                RunCount = seg.RunCount,
                TotalPieces = seg.TotalPieces,
                SpacingCm = seg.SpacingCm,
                Rqd = seg.Rqd,
                Level = Judge(seg.SpacingCm),
            };
            foreach (var run in seg.Runs)
            {
                if (!byKey.TryGetValue(run.Key, out var src)) continue;
                src.SegmentNo = no;
                var best = model.F02Source;
                if (best == null || F02Score(src) > F02Score(best))
                    model.F02Source = src;   // 多回次归并时取填得最全的那个回次的 F.0.2
            }
            preview.Segments.Add(model);
        }

        preview.SegmentNote = !thresholds.MergeRunsIntoRockSegments
            ? "未启用回次归并（一个回次 = 一个完整性分段）"
            : $"{sources.Count} 个回次 → {preview.Segments.Count} 个完整性分段" +
              $"（最小段厚 {thresholds.RockSegmentMinThicknessM:0.##}m，" +
              $"间距容差 {thresholds.RockSegmentSpacingToleranceRatio:P0}）";
    }

    /// <summary>F.0.2 六列的填写完整度，用于归并段挑选参数来源。</summary>
    private static int F02Score(ExcelImportRow r)
    {
        var score = 0;
        if (r.RockType != RockType.NotSet) score++;
        if (r.RockStructure != RockStructureType.NotSet) score++;
        if (r.Hardness != RockHardnessLevel.NotSet) score++;
        if (r.Homogeneity != RockHomogeneity.NotSet) score++;
        if (r.Groundwater != GroundwaterCondition.NotSet) score++;
        if (r.CaveAxisAngleLessThan30.HasValue) score++;
        return score;
    }

    // ====================================================================
    // 判级：只按平均间距查表 F.0.4
    // ====================================================================

    /// <summary>
    /// 判级。<b>只使用平均间距</b>：「结构面发育组数」不参与匹配（见
    /// <see cref="IntegrityCriteriaEngine.TryMatchBySpacing"/>）。
    /// 默认表已覆盖 S&gt;0 的全部区间；S≤0 说明该段没有任何结构面计数，保守按"完整性差"。
    /// </summary>
    private IntegrityLevel Judge(double spacingCm)
    {
        if (spacingCm <= 0) return IntegrityLevel.Poor;

        var criteria = _specificationService?.CurrentConfig?.IntegrityLevelCriteria;
        if (criteria == null || criteria.Count == 0)
            criteria = IntegrityCriteriaEngine.GetDefaultCriteria();

        var hit = IntegrityCriteriaEngine.TryMatchBySpacing(criteria, spacingCm);
        if (hit != null)
        {
            var level = IntegrityCriteriaEngine.ParseLevel(hit.LevelKey);
            if (level != IntegrityLevel.Unknown)
                return level;
        }

        // 用户把判定表改坏（区间写空/写错）导致无匹配时按间距兜底，避免整孔 Unknown。
        // 分档与内置默认表一致。
        return spacingCm switch
        {
            > 95 => IntegrityLevel.Intact,
            > 50 => IntegrityLevel.RelativelyIntact,
            > 30 => IntegrityLevel.RelativelyIntact,
            > 10 => IntegrityLevel.Poor,
            > 2 => IntegrityLevel.RelativelyBroken,
            _ => IntegrityLevel.Broken
        };
    }

    // ====================================================================
    // 解析辅助
    // ====================================================================
    private static double? ReadDouble(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        // ClosedXML 0.100+：单元格类型属性为 DataType（XLDataType 枚举）
        if (cell.DataType == XLDataType.Number) return cell.GetValue<double>();
        var text = cell.GetString().Trim();
        return double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? ReadInt(IXLCell cell)
    {
        var v = ReadDouble(cell);
        return v.HasValue ? (int)Math.Round(v.Value) : null;
    }

    private static void ReadF02(IXLWorksheet wsOut, int rowNumber, ExcelImportRow row)
    {
        row.RockType = ParseEnum<RockType>(wsOut.Cell(rowNumber, 11).GetString());
        row.RockStructure = ParseEnum<RockStructureType>(wsOut.Cell(rowNumber, 12).GetString());
        row.Hardness = ParseEnum<RockHardnessLevel>(wsOut.Cell(rowNumber, 13).GetString());
        row.Homogeneity = ParseEnum<RockHomogeneity>(wsOut.Cell(rowNumber, 14).GetString());
        row.Groundwater = ParseGroundwater(wsOut.Cell(rowNumber, 15).GetString());

        var cave = wsOut.Cell(rowNumber, 16).GetString().Trim();
        row.CaveAxisAngleLessThan30 = cave switch
        {
            "是" => true,
            "否" => false,
            _ => null   // 不设置 / 空 / 无法识别
        };

        row.F02Complete = row.RockType != RockType.NotSet
            && row.RockStructure != RockStructureType.NotSet
            && row.Hardness != RockHardnessLevel.NotSet
            && row.Homogeneity != RockHomogeneity.NotSet
            && row.Groundwater != GroundwaterCondition.NotSet;
    }

    /// <summary>按 [Description] 文本反查枚举；模板简写别名优先于 Description。</summary>
    private static T ParseEnum<T>(string? text, Dictionary<string, T>? aliases = null) where T : struct, Enum
    {
        var s = (text ?? string.Empty).Trim();
        if (s.Length == 0) return default;
        if (aliases != null && aliases.TryGetValue(s, out var alias)) return alias;
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var desc = field.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (desc == s || field.Name == s)
                return (T)field.GetValue(null)!;
        }
        return default;
    }

    private static GroundwaterCondition ParseGroundwater(string? text)
    {
        // 模板下拉用简写（干燥/潮湿/湿润），枚举 Description 为规范全称（潮湿/渗水 等）
        var s = (text ?? string.Empty).Trim();
        return s switch
        {
            "" => GroundwaterCondition.NotSet,
            "干燥" => GroundwaterCondition.Dry,
            "潮湿" => GroundwaterCondition.Damp,
            "湿润" => GroundwaterCondition.Wet,
            _ => ParseEnum<GroundwaterCondition>(s)
        };
    }
}

public static class EnumDescriptionExtensions
{
    public static string GetDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString()!;
    }
}

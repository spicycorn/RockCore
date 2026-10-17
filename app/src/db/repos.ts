/**
 * 数据访问层：全部 SQL 集中在这里，UI 不写 SQL。
 * 只依赖最小 Sql 接口（database.ts 把 Rust 命令 db_execute/db_select/db_transaction 适配到它），
 * 便于将来在测试里注入替身。
 */

import type { ImportRow, ImportSegment } from '../engine/excelImport';
import { autoGroundwater, boreholeCaveAngle } from '../engine/boreholeGeometry';
import {
  GroundwaterCondition,
  IntegrityLevel,
  RockClass,
  RockHardnessLevel,
  RockHomogeneity,
  RockStructureType,
  RockType,
} from '../engine/enums';

export interface Sql {
  execute(sql: string, bind?: unknown[]): Promise<unknown>;
  select<T>(sql: string, bind?: unknown[]): Promise<T[]>;
  /**
   * 单事务执行一组语句（Rust 端单连接全程持锁，事务之间不可能交错）。
   * 返回最后一条 INSERT 的自增 id；无 INSERT 时为 0。
   */
  transaction(statements: { sql: string; params?: unknown[] }[]): Promise<number>;
}

/**
 * 事务执行一组语句：整组原子提交，任一失败整体回滚并抛出原始错误。
 * 事务控制（BEGIN/COMMIT）由 Rust 端 db_transaction 负责，JS 层永远发不出裸 BEGIN，
 * "cannot start a transaction within a transaction" 这类问题在结构上不可能再发生。
 */
export async function runOps(db: Sql, ops: { query: string; values?: unknown[] }[]): Promise<void> {
  if (ops.length === 0) return;
  await db.transaction(ops.map((op) => ({ sql: op.query, params: op.values ?? [] })));
}

/** 从 execute 返回值取自增主键（db_execute 返回 { lastInsertId, rowsAffected }） */
function lastId(res: unknown): number {
  if (typeof res === 'number') return res;
  const v = (res as { lastInsertId?: unknown } | null)?.lastInsertId;
  return Number(v ?? 0);
}

// ---------------------------------------------------------------------------
// 行类型（与 schema.ts 列一一对应）
// ---------------------------------------------------------------------------

export interface ProjectRow {
  id: number;
  project_number: string;
  name: string;
  phase: string;
  cave_axis_azimuth: number | null;
  created_at: string;
}

export interface BoreholeRow {
  id: number;
  project_id: number;
  number: string;
  orifice_elevation: number | null;
  total_depth: number | null;
  /** 测量深度：仅作与 Σ回次进尺 核对的参考，不参与出图 */
  measured_depth: number | null;
  groundwater_depth: number | null;
  azimuth: number | null;
  inclination_angle: number | null;
  orifice_x: number | null;
  orifice_y: number | null;
  orifice_z: number | null;
}

export interface BatchRow {
  id: number;
  borehole_id: number;
  file_name: string;
  imported_at: string;
  start_depth: number;
  run_count: number;
}

export interface RunStatRow {
  id: number;
  batch_id: number;
  run_no: number;
  part_index: number;
  part_total: number;
  depth_start: number;
  depth_end: number;
  run_length_m: number;
  run_total_length_m: number;
  frag_count: number | null;
  frag_length_cm: number | null;
  pieces: string;
  total_pieces: number;
  joint_count: number;
  rqd: number;
  avg_spacing_cm: number;
  /** 取芯率（%）；v2 及更早的旧数据行为 null */
  recovery: number | null;
  /** 1 = 破碎段（v13 模板「长度，破碎」记录）；旧数据为 0 */
  broken: number;
}

export interface SegmentRow {
  id: number;
  borehole_id: number;
  batch_id: number;
  seq: number;
  depth_start: number;
  depth_end: number;
  thickness_m: number;
  run_count: number;
  total_pieces: number;
  spacing_cm: number;
  rqd: number;
  integrity_level: number;
  integrity_override: number | null;
  rock_type: number;
  structure_type: number;
  hardness: number;
  homogeneity: number;
  groundwater: number;
  cave_angle_lt30: number | null;
  rock_class: number | null;
  basis: string | null;
  updated_at: string;
}

/** 段的最终完整性等级：用户覆盖优先于自动判级 */
export function finalIntegrityLevel(row: SegmentRow): IntegrityLevel {
  return (row.integrity_override ?? row.integrity_level) as IntegrityLevel;
}

/** 段的 F.0.2 参数（编辑器/分类共用） */
export function segmentF02(row: SegmentRow) {
  return {
    rockType: row.rock_type as RockType,
    structureType: row.structure_type as RockStructureType,
    hardnessLevel: row.hardness as RockHardnessLevel,
    homogeneity: row.homogeneity as RockHomogeneity,
    groundwater: row.groundwater as GroundwaterCondition,
    caveAxisAngleLessThan30: row.cave_angle_lt30 == null ? null : row.cave_angle_lt30 === 1,
  };
}

/**
 * 生效 F.0.2 参数（v1.1.2）：库存覆盖值优先；
 * 地下水（0=自动）按 水位埋深+倾角+段底深度 判定，洞轴夹角（null=自动）按
 * 孔方位角+倾角+项目洞轴线方位角 计算。数据不足时保持未定，绝不伪造。
 */
export function effectiveF02(
  row: SegmentRow,
  borehole: BoreholeRow | null,
  project: ProjectRow | null,
  caveAngleThreshold = 30,
) {
  const base = segmentF02(row);
  let groundwater = base.groundwater;
  if (groundwater === GroundwaterCondition.NotSet) {
    const auto = autoGroundwater(
      row.depth_end,
      borehole?.inclination_angle ?? null,
      borehole?.groundwater_depth ?? null,
    );
    groundwater = auto == null
      ? GroundwaterCondition.NotSet
      : auto ? GroundwaterCondition.Wet : GroundwaterCondition.Dry;
  }
  const caveAxisAngleLessThan30 = base.caveAxisAngleLessThan30 ?? (
    () => {
      const angle = boreholeCaveAngle(
        borehole?.azimuth ?? null,
        borehole?.inclination_angle ?? null,
        project?.cave_axis_azimuth ?? null,
      );
      return angle == null ? null : angle < caveAngleThreshold;
    }
  )();
  return { ...base, groundwater, caveAxisAngleLessThan30 };
}

// ---------------------------------------------------------------------------
// 项目 / 钻孔
// ---------------------------------------------------------------------------

export interface ProjectInput {
  project_number: string;
  name: string;
  phase: string;
  cave_axis_azimuth: number | null;
}

export async function listProjects(db: Sql): Promise<ProjectRow[]> {
  return db.select<ProjectRow>('SELECT * FROM projects ORDER BY id DESC');
}

export async function createProject(db: Sql, p: ProjectInput): Promise<number> {
  const res = await db.execute(
    'INSERT INTO projects (project_number, name, phase, cave_axis_azimuth, created_at) VALUES (?1, ?2, ?3, ?4, ?5)',
    [p.project_number, p.name, p.phase, p.cave_axis_azimuth, new Date().toISOString()],
  );
  return lastId(res);
}

export async function updateProject(db: Sql, id: number, p: ProjectInput): Promise<void> {
  await db.execute(
    'UPDATE projects SET project_number = ?1, name = ?2, phase = ?3, cave_axis_azimuth = ?4 WHERE id = ?5',
    [p.project_number, p.name, p.phase, p.cave_axis_azimuth, id],
  );
}

export async function deleteProject(db: Sql, id: number): Promise<void> {
  // 显式级联（不依赖 PRAGMA foreign_keys 在连接池下的表现）
  await runOps(db, [
    { query: 'DELETE FROM attachments WHERE borehole_id IN (SELECT id FROM boreholes WHERE project_id = ?1)', values: [id] },
    { query: 'DELETE FROM segments WHERE borehole_id IN (SELECT id FROM boreholes WHERE project_id = ?1)', values: [id] },
    { query: 'DELETE FROM run_stats WHERE batch_id IN (SELECT id FROM import_batches WHERE borehole_id IN (SELECT id FROM boreholes WHERE project_id = ?1))', values: [id] },
    { query: 'DELETE FROM import_batches WHERE borehole_id IN (SELECT id FROM boreholes WHERE project_id = ?1)', values: [id] },
    { query: 'DELETE FROM boreholes WHERE project_id = ?1', values: [id] },
    { query: 'DELETE FROM projects WHERE id = ?1', values: [id] },
  ]);
}

export interface BoreholeInput {
  number: string;
  orifice_elevation: number | null;
  total_depth: number | null;
  measured_depth: number | null;
  groundwater_depth: number | null;
  azimuth: number | null;
  inclination_angle: number | null;
  orifice_x: number | null;
  orifice_y: number | null;
  orifice_z: number | null;
}

export async function listBoreholes(db: Sql, projectId: number): Promise<BoreholeRow[]> {
  return db.select<BoreholeRow>('SELECT * FROM boreholes WHERE project_id = ?1 ORDER BY number', [projectId]);
}

export async function createBorehole(db: Sql, projectId: number, b: BoreholeInput): Promise<number> {
  const res = await db.execute(
    `INSERT INTO boreholes (project_id, number, orifice_elevation, total_depth, measured_depth, groundwater_depth,
       azimuth, inclination_angle, orifice_x, orifice_y, orifice_z)
     VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11)`,
    [projectId, b.number, b.orifice_elevation, b.total_depth, b.measured_depth, b.groundwater_depth,
      b.azimuth, b.inclination_angle, b.orifice_x, b.orifice_y, b.orifice_z],
  );
  return lastId(res);
}

export async function updateBorehole(db: Sql, id: number, b: BoreholeInput): Promise<void> {
  await db.execute(
    `UPDATE boreholes SET number = ?1, orifice_elevation = ?2, total_depth = ?3, measured_depth = ?4, groundwater_depth = ?5,
       azimuth = ?6, inclination_angle = ?7, orifice_x = ?8, orifice_y = ?9, orifice_z = ?10
     WHERE id = ?11`,
    [b.number, b.orifice_elevation, b.total_depth, b.measured_depth, b.groundwater_depth,
      b.azimuth, b.inclination_angle, b.orifice_x, b.orifice_y, b.orifice_z, id],
  );
}

export async function deleteBorehole(db: Sql, id: number): Promise<void> {
  await runOps(db, [
    { query: 'DELETE FROM attachments WHERE borehole_id = ?1', values: [id] },
    { query: 'DELETE FROM segments WHERE borehole_id = ?1', values: [id] },
    { query: 'DELETE FROM run_stats WHERE batch_id IN (SELECT id FROM import_batches WHERE borehole_id = ?1)', values: [id] },
    { query: 'DELETE FROM import_batches WHERE borehole_id = ?1', values: [id] },
    { query: 'DELETE FROM boreholes WHERE id = ?1', values: [id] },
  ]);
}

// ---------------------------------------------------------------------------
// 导入落库（同钻孔旧批次整体替换，对齐 1.x 语义）
// ---------------------------------------------------------------------------

export interface ImportBatchInput {
  fileName: string;
  startDepth: number;
  runCount: number;
}

/**
 * 导入落库：同钻孔旧批次整体替换（对齐 1.x 语义）。
 * 事务一：删旧数据 + 插新批次（原子替换，取回 batchId）；
 * 事务二：批次明细（回次统计 + 完整性分段）。
 */
export async function saveImport(
  db: Sql,
  boreholeId: number,
  batch: ImportBatchInput,
  rows: readonly ImportRow[],
  segments: readonly ImportSegment[],
): Promise<number> {
  // 孔深 = 起始深度 + Σ回次进尺（v1.1.2：出图深度一律以进尺为准，测量深度仅核对）
  const autoTotalDepth = batch.startDepth + rows.reduce(
    (a, r) => (r.runNo != null && (r.runLengthM ?? 0) > 0 ? a + (r.runLengthM ?? 0) : a), 0,
  );
  const batchId = await db.transaction([
    { sql: 'DELETE FROM segments WHERE borehole_id = ?1', params: [boreholeId] },
    { sql: 'DELETE FROM run_stats WHERE batch_id IN (SELECT id FROM import_batches WHERE borehole_id = ?1)', params: [boreholeId] },
    { sql: 'DELETE FROM import_batches WHERE borehole_id = ?1', params: [boreholeId] },
    {
      sql: `INSERT INTO import_batches (borehole_id, file_name, imported_at, start_depth, run_count)
            VALUES (?1, ?2, ?3, ?4, ?5)`,
      params: [boreholeId, batch.fileName, new Date().toISOString(), batch.startDepth, batch.runCount],
    },
    { sql: 'UPDATE boreholes SET total_depth = ?1 WHERE id = ?2', params: [autoTotalDepth, boreholeId] },
  ]);

  const ops: { query: string; values?: unknown[] }[] = [];
  for (const r of rows) {
    if (r.runNo == null || (r.runLengthM ?? 0) <= 0) continue; // 阻断/空行不入库
    ops.push({
      query: `INSERT INTO run_stats (batch_id, run_no, part_index, part_total, depth_start, depth_end,
          run_length_m, run_total_length_m, frag_count, frag_length_cm, pieces,
          total_pieces, joint_count, rqd, avg_spacing_cm, recovery, broken)
        VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17)`,
      values: [batchId, r.runNo, r.runPartIndex, r.runRowCount, r.depthStart, r.depthEnd,
        r.runLengthM, r.runTotalLengthM, r.fragCount, r.fragLengthCm, JSON.stringify(r.pieces),
        r.totalPieces, r.jointCount, r.rqd, r.avgSpacingCm, r.recoveryPct, r.broken ? 1 : 0],
    });
  }
  for (const s of segments) {
    ops.push({
      query: `INSERT INTO segments (borehole_id, batch_id, seq, depth_start, depth_end, thickness_m,
          run_count, total_pieces, spacing_cm, rqd, integrity_level,
          rock_type, structure_type, hardness, updated_at)
        VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15)`,
      values: [boreholeId, batchId, s.no, s.depthStart, s.depthEnd, s.thicknessM,
        s.runCount, s.totalPieces, s.spacingCm, s.rqd, s.level,
        s.rockType, s.structureType, s.hardness, new Date().toISOString()],
    });
  }
  if (ops.length > 0) await runOps(db, ops);
  return batchId;
}

// ---------------------------------------------------------------------------
// 查询 / 编辑
// ---------------------------------------------------------------------------

export async function listBatches(db: Sql, boreholeId: number): Promise<BatchRow[]> {
  return db.select<BatchRow>('SELECT * FROM import_batches WHERE borehole_id = ?1 ORDER BY id DESC', [boreholeId]);
}

export async function listRunStats(db: Sql, boreholeId: number): Promise<RunStatRow[]> {
  return db.select<RunStatRow>(
    `SELECT rs.* FROM run_stats rs
     JOIN import_batches b ON b.id = rs.batch_id
     WHERE b.borehole_id = ?1
     ORDER BY rs.depth_start, rs.part_index`,
    [boreholeId],
  );
}

export async function listSegments(db: Sql, boreholeId: number): Promise<SegmentRow[]> {
  return db.select<SegmentRow>(
    'SELECT * FROM segments WHERE borehole_id = ?1 ORDER BY depth_start',
    [boreholeId],
  );
}

/** 段 F.0.2 参数 + 覆盖等级 + 分类结果的整体回写（分类在内存重算后落库） */
export interface SegmentClassificationWrite {
  id: number;
  integrityOverride: IntegrityLevel | null;
  rockType: RockType;
  structureType: RockStructureType;
  hardness: RockHardnessLevel;
  homogeneity: RockHomogeneity;
  groundwater: GroundwaterCondition;
  caveAngleLt30: boolean | null;
  rockClass: RockClass | null;
  basis: string | null;
}

export async function saveSegmentClassifications(
  db: Sql,
  writes: readonly SegmentClassificationWrite[],
): Promise<void> {
  if (writes.length === 0) return;
  const now = new Date().toISOString();
  await runOps(db, writes.map((w) => ({
    query: `UPDATE segments SET integrity_override = ?1, rock_type = ?2, structure_type = ?3,
        hardness = ?4, homogeneity = ?5, groundwater = ?6, cave_angle_lt30 = ?7,
        rock_class = ?8, basis = ?9, updated_at = ?10
      WHERE id = ?11`,
    values: [
      w.integrityOverride, w.rockType, w.structureType, w.hardness, w.homogeneity,
      w.groundwater, w.caveAngleLt30 == null ? null : w.caveAngleLt30 ? 1 : 0,
      w.rockClass, w.basis, now, w.id,
    ],
  })));
}

// ---------------------------------------------------------------------------
// 钻孔附件（岩心照片等）：base64 内嵌存储，只做预览查看，不与数据行绑定
// ---------------------------------------------------------------------------

/** 附件列表行（不含 data_b64，避免整表加载大字段） */
export interface AttachmentRow {
  id: number;
  borehole_id: number;
  file_name: string;
  mime: string;
  size_bytes: number;
  created_at: string;
}

export interface AttachmentFullRow extends AttachmentRow {
  data_b64: string;
}

export async function listAttachments(db: Sql, boreholeId: number): Promise<AttachmentRow[]> {
  return db.select<AttachmentRow>(
    `SELECT id, borehole_id, file_name, mime, size_bytes, created_at
       FROM attachments WHERE borehole_id = ?1 ORDER BY id`,
    [boreholeId],
  );
}

export async function getAttachment(db: Sql, id: number): Promise<AttachmentFullRow | null> {
  const rows = await db.select<AttachmentFullRow>('SELECT * FROM attachments WHERE id = ?1', [id]);
  return rows.length > 0 ? rows[0] : null;
}

/** 批量添加附件（多选导入）：整组一个事务，要么全部入库要么全部不入库 */
export async function addAttachments(
  db: Sql,
  boreholeId: number,
  items: readonly { fileName: string; mime: string; sizeBytes: number; dataB64: string }[],
): Promise<void> {
  await runOps(db, items.map((a) => ({
    query: `INSERT INTO attachments (borehole_id, file_name, mime, size_bytes, data_b64, created_at)
            VALUES (?1, ?2, ?3, ?4, ?5, datetime('now','localtime'))`,
    values: [boreholeId, a.fileName, a.mime, a.sizeBytes, a.dataB64],
  })));
}

export async function deleteAttachment(db: Sql, id: number): Promise<void> {
  await db.execute('DELETE FROM attachments WHERE id = ?1', [id]);
}

// ---------------------------------------------------------------------------
// 设置（JSON 值，键固定）
// ---------------------------------------------------------------------------

export async function getSetting(db: Sql, key: string): Promise<string | null> {
  const rows = await db.select<{ value: string }>('SELECT value FROM settings WHERE key = ?1', [key]);
  return rows.length > 0 ? rows[0].value : null;
}

export async function setSetting(db: Sql, key: string, value: string): Promise<void> {
  await db.execute(
    'INSERT INTO settings (key, value) VALUES (?1, ?2) ON CONFLICT(key) DO UPDATE SET value = excluded.value',
    [key, value],
  );
}

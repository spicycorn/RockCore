/**
 * SQLite 模式定义与建表（幂等 DDL + user_version 迁移）。
 * 不依赖插件的迁移机制：Rust 侧保持零业务逻辑。
 */

/** 当前模式版本；后续结构变更时递增并补 migrate() 分支 */
export const SCHEMA_VERSION = 1;

export const SCHEMA_DDL = `
CREATE TABLE IF NOT EXISTS projects (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  project_number    TEXT NOT NULL DEFAULT '',
  name              TEXT NOT NULL,
  phase             TEXT NOT NULL DEFAULT '',
  cave_axis_azimuth REAL,
  created_at        TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS boreholes (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  project_id        INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  number            TEXT NOT NULL,
  orifice_elevation REAL,
  total_depth       REAL,
  groundwater_depth REAL,
  azimuth           REAL,
  inclination_angle REAL,
  orifice_x         REAL,
  orifice_y         REAL,
  orifice_z         REAL
);

CREATE TABLE IF NOT EXISTS import_batches (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  borehole_id  INTEGER NOT NULL REFERENCES boreholes(id) ON DELETE CASCADE,
  file_name    TEXT NOT NULL,
  imported_at  TEXT NOT NULL,
  start_depth  REAL NOT NULL DEFAULT 0,
  run_count    INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS run_stats (
  id                 INTEGER PRIMARY KEY AUTOINCREMENT,
  batch_id           INTEGER NOT NULL REFERENCES import_batches(id) ON DELETE CASCADE,
  run_no             INTEGER NOT NULL,
  part_index         INTEGER NOT NULL DEFAULT 1,
  part_total         INTEGER NOT NULL DEFAULT 1,
  depth_start        REAL NOT NULL,
  depth_end          REAL NOT NULL,
  run_length_m       REAL NOT NULL,
  run_total_length_m REAL NOT NULL,
  frag_count         INTEGER,
  frag_length_cm     REAL,
  pieces             TEXT NOT NULL DEFAULT '[]',
  total_pieces       INTEGER NOT NULL,
  joint_count        INTEGER NOT NULL,
  rqd                REAL NOT NULL,
  avg_spacing_cm     REAL NOT NULL
);

CREATE TABLE IF NOT EXISTS segments (
  id                 INTEGER PRIMARY KEY AUTOINCREMENT,
  borehole_id        INTEGER NOT NULL REFERENCES boreholes(id) ON DELETE CASCADE,
  batch_id           INTEGER NOT NULL REFERENCES import_batches(id) ON DELETE CASCADE,
  seq                INTEGER NOT NULL,
  depth_start        REAL NOT NULL,
  depth_end          REAL NOT NULL,
  thickness_m        REAL NOT NULL,
  run_count          INTEGER NOT NULL,
  total_pieces       INTEGER NOT NULL,
  spacing_cm         REAL NOT NULL,
  rqd                REAL NOT NULL,
  integrity_level    INTEGER NOT NULL,
  integrity_override INTEGER,
  rock_type          INTEGER NOT NULL DEFAULT 0,
  structure_type     INTEGER NOT NULL DEFAULT 0,
  hardness           INTEGER NOT NULL DEFAULT 0,
  homogeneity        INTEGER NOT NULL DEFAULT 0,
  groundwater        INTEGER NOT NULL DEFAULT 0,
  cave_angle_lt30    INTEGER,
  rock_class         INTEGER,
  basis              TEXT,
  updated_at         TEXT
);

CREATE INDEX IF NOT EXISTS idx_boreholes_project ON boreholes(project_id);
CREATE INDEX IF NOT EXISTS idx_batches_borehole  ON import_batches(borehole_id);
CREATE INDEX IF NOT EXISTS idx_runstats_batch    ON run_stats(batch_id);
CREATE INDEX IF NOT EXISTS idx_segments_borehole ON segments(borehole_id);

CREATE TABLE IF NOT EXISTS settings (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
);
`;

/** 幂等建表 + 版本迁移入口 */
export async function ensureSchema(
  exec: (sql: string) => Promise<unknown>,
  queryVersion: () => Promise<number>,
  setVersion: (v: number) => Promise<unknown>,
): Promise<void> {
  await exec('PRAGMA foreign_keys = ON');
  const v = await queryVersion();
  if (v === 0) {
    // 全新库：直接建到当前版本
    for (const stmt of splitStatements(SCHEMA_DDL)) await exec(stmt);
    await setVersion(SCHEMA_VERSION);
    return;
  }
  // 旧库：先补建缺失表（IF NOT EXISTS 幂等），再按版本逐级迁移
  for (const stmt of splitStatements(SCHEMA_DDL)) await exec(stmt);
  if (v < SCHEMA_VERSION) {
    // 未来版本迁移分支写在这里
    await setVersion(SCHEMA_VERSION);
  }
}

/** 按分号拆分 DDL（本文件 DDL 无字符串内分号，简单拆分即可） */
export function splitStatements(sql: string): string[] {
  return sql
    .split(';')
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

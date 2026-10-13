/**
 * 数据库打开与全局应用状态装配（Tauri 运行时专用；纯逻辑都在 repos/schema）。
 */

import Database from '@tauri-apps/plugin-sql';
import { ensureSchema, SCHEMA_VERSION } from './schema';
import type { Sql } from './repos';
import { getSetting, setSetting } from './repos';
import {
  defaultConfig,
  defaultIntegrityCriteria,
  ensureDefaults,
  legacyIntegrityCriteriaV1,
  type RockSpecificationConfig,
} from '../engine/config';

const DB_FILE = 'sqlite:rockcore.db';
export const CONFIG_SETTING_KEY = 'spec_config_v1';

let dbPromise: Promise<Sql> | null = null;

/** 打开（并初始化）应用数据库，单例 */
export function openDatabase(): Promise<Sql> {
  dbPromise ??= (async () => {
    const db = await Database.load(DB_FILE);
    await ensureSchema(
      (sql) => db.execute(sql),
      async () => {
        // 插件真实签名：select<T> 返回 Promise<T>，T 写成数组类型
        const rows = await db.select<Record<string, number>[]>('PRAGMA user_version');
        const first = rows[0] as Record<string, number> | undefined;
        const key = first ? Object.keys(first)[0] : undefined;
        return first && key != null ? Number(first[key]) : 0;
      },
      (v) => db.execute(`PRAGMA user_version = ${Math.trunc(v)}`),
    );
    return db as unknown as Sql;
  })();
  return dbPromise;
}

/** 读取判定配置（缺失字段补默认；用户改过的不覆盖） */
export async function loadConfig(db: Sql): Promise<RockSpecificationConfig> {
  const raw = await getSetting(db, CONFIG_SETTING_KEY);
  if (raw == null) return defaultConfig();
  try {
    const config = ensureDefaults(JSON.parse(raw) as Partial<RockSpecificationConfig>);
    // 迁移：库里存的判定表若与旧版内置默认表逐字段相同，说明用户从未编辑过，
    // 直接换成现行规范（DL/T 5894-2025 表 F.0.4）默认表；用户改过的不动。
    if (JSON.stringify(config.integrityLevelCriteria) === JSON.stringify(legacyIntegrityCriteriaV1())) {
      config.integrityLevelCriteria = defaultIntegrityCriteria();
    }
    return config;
  } catch {
    return defaultConfig();
  }
}

export function saveConfig(db: Sql, config: RockSpecificationConfig): Promise<void> {
  return setSetting(db, CONFIG_SETTING_KEY, JSON.stringify(config));
}

export { SCHEMA_VERSION };

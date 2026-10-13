/**
 * 全局应用状态（zustand）：数据 + 动作。视图只做渲染与交互编排。
 */

import { create } from 'zustand';
import type { Sql } from '../db/repos';
import {
  createBorehole, createProject, deleteBorehole, deleteProject,
  listBatches, listBoreholes, listProjects, listRunStats, listSegments,
  saveImport, saveSegmentClassifications, updateBorehole, updateProject,
  type BoreholeInput, type BatchRow, type BoreholeRow, type ProjectInput, type ProjectRow,
  type RunStatRow, type SegmentClassificationWrite, type SegmentRow,
} from '../db/repos';
import { loadConfig, openDatabase, saveConfig } from '../db/database';
import type { RockSpecificationConfig } from '../engine/config';
import { defaultConfig } from '../engine/config';
import { applyDepthAnchor, previewWorkbook, rebuildSegments, type ImportPreview } from '../engine/excelImport';

export type TabKey = 'import' | 'runs' | 'segments' | 'column' | 'stats' | 'settings';

export interface Toast {
  id: number;
  kind: 'info' | 'success' | 'warn' | 'error';
  text: string;
}

interface AppState {
  db: Sql | null;
  config: RockSpecificationConfig;
  ready: boolean;
  initError: string | null;
  busy: string | null;

  projects: ProjectRow[];
  projectId: number | null;
  boreholes: BoreholeRow[];
  boreholeId: number | null;

  batches: BatchRow[];
  runStats: RunStatRow[];
  segments: SegmentRow[];

  activeTab: TabKey;
  toasts: Toast[];

  init(): Promise<void>;
  toast(kind: Toast['kind'], text: string): void;
  dismissToast(id: number): void;
  setBusy(text: string | null): void;
  setTab(tab: TabKey): void;

  refreshProjects(): Promise<void>;
  selectProject(id: number | null): Promise<void>;
  saveProject(id: number | null, input: ProjectInput): Promise<number | null>;
  removeProject(id: number): Promise<void>;

  refreshBoreholes(): Promise<void>;
  selectBorehole(id: number | null): Promise<void>;
  saveBorehole(id: number | null, input: BoreholeInput): Promise<number | null>;
  removeBorehole(id: number): Promise<void>;
  refreshBoreholeData(): Promise<void>;

  /** 解析 Excel → 预览（不落库）；startDepth 锚定起始深度 */
  previewFile(path: string, startDepth: number): Promise<ImportPreview>;
  /** 预览确认后落库（同钻孔旧批次整体替换） */
  commitImport(preview: ImportPreview, fileName: string, startDepth: number): Promise<void>;

  saveSegmentEdits(writes: SegmentClassificationWrite[]): Promise<void>;
  applyConfig(config: RockSpecificationConfig): Promise<void>;
}

let toastSeq = 1;

export const useApp = create<AppState>((set, get) => ({
  db: null,
  config: defaultConfig(),
  ready: false,
  initError: null,
  busy: null,

  projects: [],
  projectId: null,
  boreholes: [],
  boreholeId: null,

  batches: [],
  runStats: [],
  segments: [],

  activeTab: 'import',
  toasts: [],

  async init() {
    try {
      const db = await openDatabase();
      const config = await loadConfig(db);
      set({ db, config, ready: true });
      await get().refreshProjects();
    } catch (ex) {
      set({ initError: ex instanceof Error ? ex.message : String(ex) });
    }
  },

  toast(kind, text) {
    const id = toastSeq++;
    set((s) => ({ toasts: [...s.toasts, { id, kind, text }] }));
    window.setTimeout(() => get().dismissToast(id), kind === 'error' ? 8000 : 4000);
  },
  dismissToast(id) {
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }));
  },
  setBusy(text) { set({ busy: text }); },
  setTab(tab) { set({ activeTab: tab }); },

  async refreshProjects() {
    const db = get().db;
    if (!db) return;
    const projects = await listProjects(db);
    set({ projects });
    const { projectId } = get();
    if (projectId != null && !projects.some((p) => p.id === projectId)) {
      await get().selectProject(projects[0]?.id ?? null);
    } else if (projectId == null && projects.length > 0) {
      await get().selectProject(projects[0].id);
    }
  },

  async selectProject(id) {
    set({ projectId: id });
    await get().refreshBoreholes();
  },

  async saveProject(id, input) {
    const db = get().db;
    if (!db) return null;
    let newId = id;
    if (id == null) newId = await createProject(db, input);
    else await updateProject(db, id, input);
    await get().refreshProjects();
    if (id == null && newId != null) await get().selectProject(newId);
    return newId;
  },

  async removeProject(id) {
    const db = get().db;
    if (!db) return;
    await deleteProject(db, id);
    await get().refreshProjects();
  },

  async refreshBoreholes() {
    const db = get().db;
    const { projectId } = get();
    if (!db || projectId == null) {
      set({ boreholes: [], boreholeId: null, batches: [], runStats: [], segments: [] });
      return;
    }
    const boreholes = await listBoreholes(db, projectId);
    set({ boreholes });
    const { boreholeId } = get();
    if (boreholeId != null && !boreholes.some((b) => b.id === boreholeId)) {
      await get().selectBorehole(boreholes[0]?.id ?? null);
    } else if (boreholeId == null) {
      await get().selectBorehole(boreholes[0]?.id ?? null);
    } else {
      await get().refreshBoreholeData();
    }
  },

  async selectBorehole(id) {
    set({ boreholeId: id });
    await get().refreshBoreholeData();
  },

  async refreshBoreholeData() {
    const db = get().db;
    const { boreholeId } = get();
    if (!db || boreholeId == null) {
      set({ batches: [], runStats: [], segments: [] });
      return;
    }
    const [batches, runStats, segments] = await Promise.all([
      listBatches(db, boreholeId),
      listRunStats(db, boreholeId),
      listSegments(db, boreholeId),
    ]);
    set({ batches, runStats, segments });
  },

  async saveBorehole(id, input) {
    const db = get().db;
    const { projectId } = get();
    if (!db || projectId == null) return null;
    let newId = id;
    if (id == null) newId = await createBorehole(db, projectId, input);
    else await updateBorehole(db, id, input);
    await get().refreshBoreholes();
    if (id == null && newId != null) await get().selectBorehole(newId);
    return newId;
  },

  async removeBorehole(id) {
    const db = get().db;
    if (!db) return;
    await deleteBorehole(db, id);
    await get().refreshBoreholes();
  },

  async previewFile(path, startDepth) {
    const { db, config } = get();
    if (!db) throw new Error('数据库未就绪');
    const { readFileBytes } = await import('../lib/tauri');
    const buffer = await readFileBytes(path);
    const preview = await previewWorkbook(buffer, config);
    if (!preview.error && startDepth !== 0) {
      applyDepthAnchor(preview, startDepth);
      rebuildSegments(preview, preview.rows.filter((r) => r.status !== 'blocked'), config);
    }
    return preview;
  },

  async commitImport(preview, fileName, startDepth) {
    const db = get().db;
    const { boreholeId, config } = get();
    if (!db || boreholeId == null) {
      get().toast('error', '请先在左侧选择一个钻孔再导入');
      return;
    }
    applyDepthAnchor(preview, startDepth);
    rebuildSegments(preview, preview.rows.filter((r) => r.status !== 'blocked'), config);
    const runCount = new Set(preview.rows.filter((r) => r.status !== 'blocked' && r.runNo != null).map((r) => r.runNo)).size;
    await saveImport(db, boreholeId, { fileName, startDepth, runCount }, preview.rows, preview.segments);
    await get().refreshBoreholeData();
    get().toast('success', `导入完成：${preview.segments.length} 个完整性分段`);
  },

  async saveSegmentEdits(writes) {
    const db = get().db;
    if (!db) return;
    await saveSegmentClassifications(db, writes);
    await get().refreshBoreholeData();
  },

  async applyConfig(config) {
    const db = get().db;
    if (!db) return;
    await saveConfig(db, config);
    set({ config });
  },
}));

/**
 * 全局导入流程：任何入口（工具栏按钮 / 侧栏右键 / 拖拽 / 菜单 / Ctrl+I）都走这里。
 * 选文件 → 解析预览（可改起始深度实时重算）→ 确认后落库。
 */

import { useEffect, useRef, useState } from 'react';
import { AppDialog, Badge, Button } from './ui';
import { pickExcelFile } from '../lib/dialogs';
import { useApp } from '../store/app';
import { depthText, fragText, runNoText, spacingText, statusText, type ImportPreview, type ImportRow } from '../engine/excelImport';
import {
  RockHardnessLevel, RockStructureType, RockType,
  integrityLevelLabel, rockHardnessLabel, rockStructureLabel, rockTypeLabel,
} from '../engine/enums';
import { isTauri } from '../lib/tauri';

type Trigger = (path?: string) => void;
let trigger: Trigger | null = null;

/** 启动导入流程；path 省略时弹文件选择框 */
export function startImport(path?: string): void {
  trigger?.(path);
}

export function ImportFlowHost() {
  const [open, setOpen] = useState(false);
  const [path, setPath] = useState<string | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [startDepth, setStartDepth] = useState('0');
  const [onlyProblems, setOnlyProblems] = useState(false);
  const [working, setWorking] = useState(false);
  const startDepthRef = useRef(startDepth);
  startDepthRef.current = startDepth;

  const boreholeId = useApp((s) => s.boreholeId);
  const boreholes = useApp((s) => s.boreholes);
  const toast = useApp((s) => s.toast);

  async function parse(p: string, depth: number): Promise<ImportPreview> {
    return useApp.getState().previewFile(p, depth);
  }

  async function run(p: string): Promise<void> {
    setWorking(true);
    try {
      const pv = await parse(p, Number(startDepthRef.current) || 0);
      setPath(p);
      setPreview(pv);
      setOpen(true);
    } catch (ex) {
      toast('error', `导入失败：${ex instanceof Error ? ex.message : String(ex)}`);
    } finally {
      setWorking(false);
    }
  }

  useEffect(() => {
    trigger = async (p?: string) => {
      if (useApp.getState().boreholeId == null) {
        toast('warn', '请先在左侧选择（或新建）一个钻孔，再导入数据');
        return;
      }
      const file = p ?? await pickExcelFile();
      if (file != null) await run(file);
    };
    return () => { trigger = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /** 改起始深度 → 用缓存字节重新解析（保持实时一致，不存中间态） */
  async function reparse(depth: number): Promise<void> {
    if (path == null) return;
    const pv = await parse(path, depth);
    setPreview(pv);
  }

  const blockedCount = preview?.rows.filter((r) => r.status === 'blocked').length ?? 0;
  const warnCount = preview?.rows.filter((r) => r.status === 'warning').length ?? 0;
  const canImport = preview != null && preview.error == null && blockedCount === 0 && boreholeId != null;
  const selectedBorehole = boreholes.find((b) => b.id === boreholeId) ?? null;
  const boreholeMismatch = preview?.boreholeNo != null
    && selectedBorehole != null && selectedBorehole.number !== preview.boreholeNo;

  const visibleRows = preview == null ? [] :
    onlyProblems ? preview.rows.filter((r) => r.status !== 'ok') : preview.rows;

  return (
    <AppDialog
      open={open}
      onOpenChange={(o) => { if (!working) setOpen(o); }}
      title={`导入预览 — ${path?.split(/[\\/]/).pop() ?? ''}`}
      wide
      footer={
        <>
          <span className="mr-auto text-xs text-slate-500">
            {preview?.error != null
              ? <span className="text-red-600">{preview.error}</span>
              : `${preview?.rows.length ?? 0} 行 · 阻断 ${blockedCount} · 警告 ${warnCount} · ${preview?.segmentNote ?? ''}`}
          </span>
          <Button onClick={() => setOpen(false)}>取消</Button>
          <Button
            variant="primary"
            disabled={!canImport || working}
            title={blockedCount > 0 ? '存在阻断问题行，修正后重新导入' : '写入数据库（替换该钻孔旧批次）'}
            onClick={async () => {
              if (preview == null || !canImport) return;
              setWorking(true);
              try {
                await useApp.getState().commitImport(preview, path?.split(/[\\/]/).pop() ?? '', Number(startDepth) || 0);
                setOpen(false);
              } catch (ex) {
                toast('error', `写入失败：${ex instanceof Error ? ex.message : String(ex)}`);
              } finally {
                setWorking(false);
              }
            }}
          >
            确认导入
          </Button>
        </>
      }
    >
      {preview != null && preview.error == null && (
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-3 text-[13px]">
            <label className="flex items-center gap-1.5">
              起始深度(m)
              <input
                type="number"
                className="h-7 w-24 rounded border border-line px-2 text-right"
                value={startDepth}
                step="0.001"
                onChange={(e) => setStartDepth(e.target.value)}
                onBlur={() => { void reparse(Number(startDepth) || 0); }}
                onKeyDown={(e) => { if (e.key === 'Enter') void reparse(Number(startDepth) || 0); }}
              />
            </label>
            <label className="flex items-center gap-1 text-slate-600">
              <input type="checkbox" checked={onlyProblems} onChange={(e) => setOnlyProblems(e.target.checked)} />
              只看问题行
            </label>
            {preview.boreholeNo != null && (
              <span
                className={boreholeMismatch ? 'text-[13px] font-medium text-amber-600' : 'text-[13px] text-slate-600'}
                title={boreholeMismatch ? 'Excel 内填写的钻孔编号与当前选中钻孔不同，数据将写入当前选中钻孔' : '来自 Excel「基本信息」表'}
              >
                Excel 钻孔编号：{preview.boreholeNo}
                {boreholeMismatch && `（当前选中「${selectedBorehole?.number}」，将导入到当前选中钻孔）`}
              </span>
            )}
            <span className="text-xs text-slate-400">
              确认导入将替换该钻孔的全部旧数据（回次记录与分段一并重建）
            </span>
          </div>
          <div className="max-h-[52vh] overflow-auto border border-line rounded">
            <table className="rc-table">
              <thead>
                <tr>
                  <th>状态</th><th>回次</th><th>深度 (m)</th><th className="rc-num">本段厚(m)</th>
                  <th className="rc-num">回次进尺(m)</th><th className="rc-num">n</th>
                  <th className="rc-num">S(cm)</th><th className="rc-num">RQD(%)</th>
                  <th>完整性</th><th>岩质/结构/坚硬</th><th className="rc-num">碎屑 长/块</th><th className="rc-num">分段</th><th>提示</th>
                </tr>
              </thead>
              <tbody>
                {visibleRows.map((r, i) => (
                  <PreviewRow key={`${r.rowNumber}-${r.runPartIndex}-${i}`} row={r} />
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {preview != null && preview.error != null && (
        <div className="text-red-600 leading-6">{preview.error}</div>
      )}
    </AppDialog>
  );
}

const statusColor: Record<string, string> = { ok: '#4b9e5f', warning: '#d97706', blocked: '#dc2626' };

/** 选填三列的完整悬停文本 */
function paramTitle(row: ImportRow): string {
  const parts = [
    row.rockType !== RockType.NotSet ? `岩质：${rockTypeLabel[row.rockType]}` : null,
    row.structureType !== RockStructureType.NotSet ? `结构：${rockStructureLabel[row.structureType]}` : null,
    row.hardness !== RockHardnessLevel.NotSet ? `坚硬程度：${rockHardnessLabel[row.hardness]}` : null,
  ].filter((s): s is string => s != null);
  return parts.length > 0 ? parts.join('　') : '岩质/结构/坚硬程度未填（导入后可在分段编辑补录）';
}

function PreviewRow({ row }: { row: ImportRow }) {
  return (
    <tr className={row.status === 'warning' ? 'rc-row-warning' : row.status === 'blocked' ? 'rc-row-blocked' : undefined}>
      <td>
        <span className="inline-flex items-center gap-1" style={{ color: statusColor[row.status] }}>
          {row.status === 'ok' ? '✓' : row.status === 'warning' ? '⚠' : '✕'} {statusText(row.status)}
        </span>
      </td>
      <td>{runNoText(row)}</td>
      <td className="rc-num">{depthText(row)}</td>
      <td className="rc-num">{row.runLengthM?.toFixed(3) ?? '—'}</td>
      <td className="rc-num">{row.runTotalLengthM?.toFixed(3) ?? '—'}</td>
      <td className="rc-num">{row.totalPieces}</td>
      <td className="rc-num">{spacingText(row.avgSpacingCm)}</td>
      <td className="rc-num">{row.rqd?.toFixed(2) ?? '—'}</td>
      <td>{row.level !== 0 ? <Badge color="#5b7fae">{integrityLevelLabel[row.level]}</Badge> : '—'}</td>
      <td className="whitespace-nowrap text-xs" title={paramTitle(row)}>
        {row.rockType !== RockType.NotSet ? rockTypeLabel[row.rockType] : ''}
        {row.structureType !== RockStructureType.NotSet && `${row.rockType !== RockType.NotSet ? '·' : ''}${rockStructureLabel[row.structureType]}`}
        {row.hardness !== RockHardnessLevel.NotSet && `·${rockHardnessLabel[row.hardness]}`}
        {row.rockType === RockType.NotSet && row.structureType === RockStructureType.NotSet
          && row.hardness === RockHardnessLevel.NotSet && '—'}
      </td>
      <td className="rc-num">{fragText(row)}</td>
      <td className="rc-num">{row.segmentNo > 0 ? row.segmentNo : '—'}</td>
      <td className="whitespace-normal text-xs text-slate-500">{row.messages.join('；')}</td>
    </tr>
  );
}

/** 供拖拽入口复用：Tauri 原生文件拖放给出真实路径，直接进同一条导入管线 */
export function useGlobalDrop(): void {
  useEffect(() => {
    let unlisten: (() => void) | null = null;
    let cancelled = false;
    void (async () => {
      if (!isTauri()) return;
      const { getCurrentWebview } = await import('@tauri-apps/api/webview');
      const fn = await getCurrentWebview().onDragDropEvent((event) => {
        if (event.payload.type !== 'drop' && event.payload.type !== 'enter') return;
        if (event.payload.type === 'enter') return; // 悬停不处理
        const file = event.payload.paths.find((p) => p.toLowerCase().endsWith('.xlsx'));
        if (file != null) startImport(file);
      });
      if (cancelled) fn(); else unlisten = fn;
    })();
    return () => { cancelled = true; unlisten?.(); };
  }, []);
}

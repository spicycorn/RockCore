/**
 * 回次明细：入库后的回次记录段（只读）。每行 = 一个记录段，
 * 拆段回次显示 回次-序；碎屑段显示 长/块。右键行可按段号过滤。
 */

import { useMemo, useState } from 'react';
import { EmptyState, showContextMenu } from '../components/ui';
import { useApp } from '../store/app';
import type { RunStatRow } from '../db/repos';

export function RunStatsView() {
  const runStats = useApp((s) => s.runStats);
  const boreholeId = useApp((s) => s.boreholeId);
  const [filterRun, setFilterRun] = useState<number | null>(null);

  const rows = useMemo(
    () => (filterRun == null ? runStats : runStats.filter((r) => r.run_no === filterRun)),
    [runStats, filterRun],
  );

  if (boreholeId == null) return <EmptyState title="请先选择钻孔" />;
  if (runStats.length === 0) {
    return <EmptyState title="该钻孔还没有回次数据" hint="导入 Excel 后，这里显示每个回次记录段的原始量与派生指标。" />;
  }

  const rowMenu = (e: React.MouseEvent, r: RunStatRow) => {
    e.preventDefault();
    showContextMenu(e.clientX, e.clientY, [
      { label: `只看回次 ${r.run_no}`, action: () => setFilterRun(r.run_no) },
      { label: '显示全部回次', disabled: filterRun == null, action: () => setFilterRun(null) },
    ]);
  };

  return (
    <div className="h-full flex flex-col">
      <div className="flex items-center gap-2 px-3 py-2 border-b border-line bg-surface text-[13px]">
        <span className="text-slate-600">共 {runStats.length} 个记录段</span>
        {filterRun != null && (
          <button type="button" className="rounded-full border border-primary-300 bg-primary-50 px-2 h-6 text-primary-600" onClick={() => setFilterRun(null)}>
            只看回次 {filterRun} ✕
          </button>
        )}
        <span className="ml-auto text-xs text-slate-400">n = ≥10cm 完整段数 + 碎屑块数 · S = 段厚÷n · RQD = ΣL÷段厚</span>
      </div>
      <div className="flex-1 overflow-auto bg-surface">
        <table className="rc-table">
          <thead>
            <tr>
              <th>回次</th><th>深度 (m)</th><th className="rc-num">本段厚(m)</th><th className="rc-num">回次进尺(m)</th>
              <th className="rc-num">n</th><th className="rc-num">节理数 n-1</th>
              <th className="rc-num">S (cm)</th><th className="rc-num">RQD (%)</th>
              <th className="rc-num">碎屑 长/块</th><th>≥10cm 段长 (cm)</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => {
              const pieces = parsePieces(r.pieces);
              return (
                <tr key={r.id} onContextMenu={(e) => rowMenu(e, r)} title="右键：按回次过滤">
                  <td>{r.part_total > 1 ? `${r.run_no}-${r.part_index}` : r.run_no}</td>
                  <td className="rc-num">{r.depth_start.toFixed(2)} – {r.depth_end.toFixed(2)}</td>
                  <td className="rc-num">{r.run_length_m.toFixed(3)}</td>
                  <td className="rc-num">{r.run_total_length_m.toFixed(3)}</td>
                  <td className="rc-num">{r.total_pieces}</td>
                  <td className="rc-num">{r.joint_count}</td>
                  <td className="rc-num">{r.avg_spacing_cm.toFixed(2)}</td>
                  <td className="rc-num">{r.rqd.toFixed(2)}</td>
                  <td className="rc-num">{r.frag_count == null ? '—' : `${(r.frag_length_cm ?? 0).toFixed(1)}/${r.frag_count}`}</td>
                  <td className="text-xs text-slate-500">{pieces.length > 0 ? pieces.map((p) => p.toFixed(1)).join('，') : '—'}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function parsePieces(json: string): number[] {
  try {
    const v: unknown = JSON.parse(json);
    return Array.isArray(v) ? v.filter((x): x is number => typeof x === 'number') : [];
  } catch {
    return [];
  }
}

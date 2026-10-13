/**
 * 统计视图：围岩类别 / 完整性占比（按厚度加权）、RQD 分布，成果表导出（Excel / CSV）。
 */

import { useMemo } from 'react';
import ExcelJS from 'exceljs';
import { useApp } from '../store/app';
import { finalIntegrityLevel, segmentF02, type SegmentRow } from '../db/repos';
import {
  groundwaterLabel, integrityLevelLabel, rockClassText, rockHardnessLabel,
  rockHomogeneityLabel, rockStructureLabel, rockTypeLabel,
  IntegrityLevel, RockClass,
} from '../engine/enums';
import { integrityColor, rockClassColor } from '../lib/colors';
import { useChart } from '../lib/chart';
import { Button, EmptyState, showContextMenu } from '../components/ui';
import { saveFileAs } from '../lib/dialogs';
import { writeFileBytes } from '../lib/tauri';

const RQD_BINS = ['0~25', '25~50', '50~75', '75~100'] as const;

function rqdBin(rqd: number): number {
  return rqd < 25 ? 0 : rqd < 50 ? 1 : rqd < 75 ? 2 : 3;
}

function caveText(v: number | null): string {
  return v == null ? '未定' : v === 1 ? '是' : '否';
}

/** 成果表行（导出与表格共用一套口径） */
function exportRows(segments: readonly SegmentRow[]): string[][] {
  return segments.map((s) => {
    const f02 = segmentF02(s);
    return [
      String(s.seq),
      s.depth_start.toFixed(2),
      s.depth_end.toFixed(2),
      s.thickness_m.toFixed(2),
      String(s.total_pieces),
      s.spacing_cm.toFixed(2),
      s.rqd.toFixed(2),
      integrityLevelLabel[finalIntegrityLevel(s)],
      rockTypeLabel[f02.rockType],
      rockStructureLabel[f02.structureType],
      rockHardnessLabel[f02.hardnessLevel],
      rockHomogeneityLabel[f02.homogeneity],
      groundwaterLabel[f02.groundwater],
      caveText(s.cave_angle_lt30),
      s.rock_class == null ? '未分类' : rockClassText(s.rock_class as RockClass),
      s.basis ?? '',
    ];
  });
}

const EXPORT_HEADERS = [
  '段号', '顶深(m)', '底深(m)', '厚度(m)', '回次段数n', '平均间距S(cm)', 'RQD(%)',
  '完整性', '岩质', '结构类型', '坚硬程度', '均一性', '地下水', '洞轴夹角<30°', '围岩类别', '分类依据',
];

export function StatsView() {
  const borehole = useApp((s) => s.boreholes.find((b) => b.id === s.boreholeId) ?? null);
  const segments = useApp((s) => s.segments);
  const toast = useApp((s) => s.toast);

  const stats = useMemo(() => {
    if (segments.length === 0) return null;
    const totalThickness = segments.reduce((a, s) => a + s.thickness_m, 0);
    const weightedRqd = totalThickness > 0
      ? segments.reduce((a, s) => a + s.rqd * s.thickness_m, 0) / totalThickness
      : 0;
    const classified = segments.filter((s) => s.rock_class != null).length;

    const clsSum = new Map<string, { value: number; color: string }>();
    const integSum = new Map<string, { value: number; color: string }>();
    const bins = RQD_BINS.map(() => 0);
    for (const s of segments) {
      const clsKey = s.rock_class == null ? '未分类' : rockClassText(s.rock_class as RockClass);
      const clsColor = s.rock_class == null ? '#9ca3af' : rockClassColor(s.rock_class as RockClass);
      const c = clsSum.get(clsKey) ?? { value: 0, color: clsColor };
      c.value += s.thickness_m; clsSum.set(clsKey, c);

      const lvl = finalIntegrityLevel(s);
      const iKey = integrityLevelLabel[lvl];
      const iColor = lvl === IntegrityLevel.Unknown ? '#9ca3af' : integrityColor(lvl);
      const ic = integSum.get(iKey) ?? { value: 0, color: iColor };
      ic.value += s.thickness_m; integSum.set(iKey, ic);

      bins[rqdBin(s.rqd)] += s.thickness_m;
    }
    return { totalThickness, weightedRqd, classified, clsSum, integSum, bins };
  }, [segments]);

  const pieOption = (title: string, data: Iterable<[string, { value: number; color: string }]>) => ({
    animation: false,
    title: { text: title, left: 'center', top: 6, textStyle: { fontSize: 13, color: '#334155' } },
    tooltip: { trigger: 'item', formatter: '{b}：{c} m（{d}%）', confine: true },
    series: [{
      type: 'pie',
      radius: ['38%', '62%'],
      center: ['50%', '56%'],
      label: { formatter: '{b} {d}%', fontSize: 11 },
      data: [...data].filter(([, v]) => v.value > 0).map(([name, v]) => ({
        name, value: Number(v.value.toFixed(2)), itemStyle: { color: v.color },
      })),
    }],
  });

  const clsChart = useChart<HTMLDivElement>(stats == null ? null : pieOption('围岩类别占比（按厚度）', stats.clsSum));
  const integChart = useChart<HTMLDivElement>(stats == null ? null : pieOption('完整性占比（按厚度）', stats.integSum));
  const rqdChart = useChart<HTMLDivElement>(stats == null ? null : {
    animation: false,
    title: { text: 'RQD 分布（按厚度）', left: 'center', top: 6, textStyle: { fontSize: 13, color: '#334155' } },
    tooltip: { trigger: 'axis', formatter: (p: { name: string; value: number }[]) => `RQD ${p[0].name}：${p[0].value} m`, confine: true },
    grid: { left: 56, right: 24, top: 44, bottom: 28 },
    xAxis: { type: 'category', data: [...RQD_BINS], axisLabel: { fontSize: 11 } },
    yAxis: { type: 'value', name: '厚度(m)', nameTextStyle: { fontSize: 10, color: '#64748b' }, axisLabel: { fontSize: 10 } },
    series: [{ type: 'bar', barWidth: '55%', itemStyle: { color: '#2f5b8f', borderRadius: [3, 3, 0, 0] }, data: stats.bins.map((v) => Number(v.toFixed(2))) }],
  });

  const exportExcel = async () => {
    if (borehole == null || segments.length === 0) return;
    const path = await saveFileAs(`分段成果_${borehole.number}.xlsx`, ['xlsx']);
    if (path == null) return;
    const wb = new ExcelJS.Workbook();
    const ws = wb.addWorksheet('分段成果');
    ws.addRow(EXPORT_HEADERS);
    ws.getRow(1).font = { bold: true, color: { argb: 'FFFFFFFF' } };
    ws.getRow(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1E3A5F' } };
    for (const r of exportRows(segments)) ws.addRow(r);
    ws.columns.forEach((col) => { col.width = 12; });
    ws.getColumn(16).width = 42;
    ws.views = [{ state: 'frozen', ySplit: 1 }];
    const buf = await wb.xlsx.writeBuffer();
    await writeFileBytes(path, new Uint8Array(buf as ArrayBuffer));
    toast('success', `已导出 ${path}`);
  };

  const exportCsv = async () => {
    if (borehole == null || segments.length === 0) return;
    const path = await saveFileAs(`分段成果_${borehole.number}.csv`, ['csv']);
    if (path == null) return;
    const esc = (v: string) => (/[",\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
    const text = '\ufeff' + [EXPORT_HEADERS, ...exportRows(segments)].map((r) => r.map(esc).join(',')).join('\r\n');
    await writeFileBytes(path, new TextEncoder().encode(text));
    toast('success', `已导出 ${path}`);
  };

  if (borehole == null) return <EmptyState title="统计" hint="请先在左侧选择钻孔。" />;
  if (stats == null) return <EmptyState title="该孔暂无分段数据" hint="导入 Excel 后自动生成。" />;

  return (
    <div className="h-full flex flex-col">
      <div
        className="flex items-center gap-2 px-3 py-2 border-b border-line bg-surface"
        onContextMenu={(e) => {
          e.preventDefault();
          showContextMenu(e.clientX, e.clientY, [
            { label: '导出 Excel 成果表…', action: () => void exportExcel() },
            { label: '导出 CSV…', action: () => void exportCsv() },
          ]);
        }}
      >
        <span className="text-sm text-slate-600">
          共 <b>{segments.length}</b> 段 · 总厚 <b>{stats.totalThickness.toFixed(2)}</b> m ·
          加权平均 RQD <b>{stats.weightedRqd.toFixed(1)}</b> % ·
          已分类 <b>{stats.classified}</b>/{segments.length}
        </span>
        <span className="ml-auto flex gap-2">
          <Button onClick={() => void exportCsv()}>导出 CSV</Button>
          <Button variant="primary" onClick={() => void exportExcel()}>导出 Excel 成果表</Button>
        </span>
      </div>
      <div className="flex-1 min-h-0 grid grid-cols-3 gap-3 p-3">
        <div className="rounded-lg border border-line bg-surface min-h-0"><div ref={clsChart.ref} className="w-full h-full" /></div>
        <div className="rounded-lg border border-line bg-surface min-h-0"><div ref={integChart.ref} className="w-full h-full" /></div>
        <div className="rounded-lg border border-line bg-surface min-h-0"><div ref={rqdChart.ref} className="w-full h-full" /></div>
      </div>
    </div>
  );
}

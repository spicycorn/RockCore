/**
 * 柱状剖面图：按真实倾角绘制孔轴线，分段沿轴着色（围岩类别 > 完整性 > 岩性，RQD 折线最外）。
 * 倾角语义（沿用 1.x）：相对水平面角度，90=竖直；0/未填/越界按竖直处理。
 * 平面布置图见 PlanView.buildPlanOption（孔口散点 + 洞轴线方位）。
 */

import { useMemo, useState } from 'react';
import { useApp } from '../store/app';
import { finalIntegrityLevel, type SegmentRow } from '../db/repos';
import {
  integrityLevelLabel, rockClassNumeral, rockClassText,
  RockClass, RockType,
} from '../engine/enums';
import { integrityColor, rockClassColor, rgb } from '../lib/colors';
import { dataUrlToBytes, useChart } from '../lib/chart';
import { Button, EmptyState, showContextMenu } from '../components/ui';
import { saveFileAs } from '../lib/dialogs';
import { writeFileBytes } from '../lib/tauri';
import { buildPlanOption } from './PlanView';

/** 沿轴法向的色带布局（px，自轴向外）：类别 10–46，完整性 46–70，岩性 70–86，RQD 96–136 */
const BAND = { cls: [10, 46], integ: [46, 70], lith: [70, 86], rqdBase: 96, rqdSpan: 40 } as const;

const lithColor = (t: number): string =>
  t === RockType.HardRock ? rgb(100, 149, 237)
    : t === RockType.SoftRock ? rgb(210, 180, 140)
      : '#d1d5db';

type Pt = number[];
type CoordApi = { coord: (v: number[]) => Pt };

/** 轴左法向的矩形带四角（像素空间） */
function bandPoly(p1: Pt, p2: Pt, inner: number, outer: number): Pt[] {
  const dx = p2[0] - p1[0], dy = p2[1] - p1[1];
  const len = Math.hypot(dx, dy) || 1;
  const nx = (-dy / len) * inner, ny = (dx / len) * inner;
  const mx = (-dy / len) * outer, my = (dx / len) * outer;
  return [
    [p1[0] + nx, p1[1] + ny],
    [p2[0] + nx, p2[1] + ny],
    [p2[0] + mx, p2[1] + my],
    [p1[0] + mx, p1[1] + my],
  ];
}

function segTooltip(s: SegmentRow): string {
  const cls = s.rock_class == null ? '未分类' : rockClassText(s.rock_class as RockClass);
  const lvl = finalIntegrityLevel(s);
  const lines = [
    `<b>段 ${s.seq}　${s.depth_start.toFixed(1)} – ${s.depth_end.toFixed(1)} m</b>（厚 ${s.thickness_m.toFixed(2)} m）`,
    `n=${s.total_pieces}　S=${s.spacing_cm.toFixed(2)} cm　RQD=${s.rqd.toFixed(2)} %`,
    `完整性：${integrityLevelLabel[lvl]}${s.integrity_override != null ? '（手动覆盖）' : ''}`,
    `围岩类别：${cls}`,
  ];
  if (s.basis) lines.push(`<span style="color:#64748b">${s.basis}</span>`);
  return lines.join('<br/>');
}

export function ColumnView() {
  const borehole = useApp((s) => s.boreholes.find((b) => b.id === s.boreholeId) ?? null);
  const boreholes = useApp((s) => s.boreholes);
  const project = useApp((s) => s.projects.find((p) => p.id === s.projectId) ?? null);
  const segments = useApp((s) => s.segments);
  const toast = useApp((s) => s.toast);

  const [mode, setMode] = useState<'profile' | 'plan'>('profile');
  const [showInteg, setShowInteg] = useState(true);
  const [showLith, setShowLith] = useState(true);
  const [showRqd, setShowRqd] = useState(true);

  const dipDeg = borehole?.inclination_angle ?? 0;
  const dip = dipDeg <= 0 || dipDeg > 90 ? 90 : dipDeg;

  const profileOption = useMemo(() => {
    if (mode !== 'profile' || borehole == null || segments.length === 0) return null;
    const rad = (dip * Math.PI) / 180;
    const cos = Math.cos(rad), sin = Math.sin(rad);
    const pt = (d: number): [number, number] => [d * cos, d * sin];
    const maxD = Math.max(...segments.map((s) => s.depth_end), borehole.total_depth ?? 0);
    if (maxD <= 0) return null;

    const bandData = segments.map((s) => ({ value: pt((s.depth_start + s.depth_end) / 2) }));
    const tickStep = maxD <= 20 ? 2 : maxD <= 60 ? 5 : maxD <= 150 ? 10 : maxD <= 400 ? 25 : 50;
    const ticks: number[] = [];
    for (let d = 0; d <= maxD + 1e-6; d += tickStep) ticks.push(d);

    const bandSeries = (colorOf: (s: SegmentRow) => string, range: readonly number[], labelOf?: (s: SegmentRow) => string | null) => ({
      type: 'custom' as const,
      coordinateSystem: 'cartesian2d' as const,
      data: bandData,
      z: 3,
      tooltip: { formatter: (p: { dataIndex: number }) => segTooltip(segments[p.dataIndex]) },
      renderItem: (p: { dataIndex: number }, api: CoordApi) => {
        const s = segments[p.dataIndex];
        const c1 = api.coord(pt(s.depth_start));
        const c2 = api.coord(pt(s.depth_end));
        const poly = bandPoly(c1, c2, range[0], range[1]);
        const children: object[] = [{
          type: 'polygon',
          shape: { points: poly },
          style: { fill: colorOf(s), stroke: '#ffffff', lineWidth: 0.5 },
        }];
        if (labelOf != null && Math.hypot(c2[0] - c1[0], c2[1] - c1[1]) > 16) {
          const label = labelOf(s);
          if (label != null) {
            const cx = poly.reduce((a, q) => a + q[0], 0) / 4;
            const cy = poly.reduce((a, q) => a + q[1], 0) / 4;
            children.push({ type: 'text', x: cx - 6, y: cy - 7, style: { text: label, fill: '#1f2937', font: 'bold 11px sans-serif' } });
          }
        }
        return { type: 'group', children };
      },
    });

    const series: object[] = [
      {
        // 深度刻度（轴右侧）
        type: 'custom', coordinateSystem: 'cartesian2d', data: [{ value: pt(maxD) }], silent: true, z: 2,
        renderItem: (_p: unknown, api: CoordApi) => {
          const children: object[] = [];
          for (const d of ticks) {
            const c = api.coord(pt(d));
            children.push(
              { type: 'line', shape: { x1: c[0] - 6, y1: c[1], x2: c[0] + 6, y2: c[1] }, style: { stroke: '#94a3b8', lineWidth: 1 } },
              { type: 'text', x: c[0] + 10, y: c[1] - 6, style: { text: `${Math.round(d)}`, fill: '#64748b', font: '10px sans-serif' } },
            );
          }
          return { type: 'group', children };
        },
      },
      {
        // 孔轴线 + 孔口/孔底/水位
        type: 'custom', coordinateSystem: 'cartesian2d', data: [{ value: pt(maxD) }], silent: true, z: 4,
        renderItem: (_p: unknown, api: CoordApi) => {
          const a = api.coord(pt(0));
          const b = api.coord(pt(maxD));
          const children: object[] = [
            { type: 'line', shape: { x1: a[0], y1: a[1], x2: b[0], y2: b[1] }, style: { stroke: '#334155', lineWidth: 2 } },
            { type: 'circle', shape: { cx: a[0], cy: a[1], r: 4 }, style: { fill: '#334155' } },
            { type: 'text', x: a[0] + 8, y: a[1] - 18, style: { text: '孔口', fill: '#334155', font: 'bold 11px sans-serif' } },
            { type: 'text', x: b[0] + 8, y: b[1] + 4, style: { text: `孔底 ${maxD.toFixed(1)}m · 倾角 ${dip}°`, fill: '#334155', font: '11px sans-serif' } },
          ];
          const gw = borehole.groundwater_depth;
          if (gw != null && gw > 0 && gw <= maxD) {
            const g = api.coord(pt(gw));
            children.push(
              { type: 'line', shape: { x1: g[0] - 14, y1: g[1], x2: g[0] + 14, y2: g[1] }, style: { stroke: '#2563eb', lineWidth: 2 } },
              { type: 'text', x: g[0] + 18, y: g[1] - 5, style: { text: `水位 ${gw}m`, fill: '#2563eb', font: '10px sans-serif' } },
            );
          }
          return { type: 'group', children };
        },
      },
      bandSeries(
        (s) => rockClassColor(s.rock_class == null ? undefined : s.rock_class as RockClass),
        BAND.cls,
        (s) => (s.rock_class == null ? null : rockClassNumeral(s.rock_class as RockClass)),
      ),
    ];
    if (showInteg) series.push(bandSeries((s) => integrityColor(finalIntegrityLevel(s)), BAND.integ));
    if (showLith) series.push(bandSeries((s) => lithColor(s.rock_type), BAND.lith));
    if (showRqd) {
      series.push({
        type: 'custom', coordinateSystem: 'cartesian2d', data: [{ value: pt(maxD) }], silent: true, z: 5,
        renderItem: (_p: unknown, api: CoordApi) => {
          const a = api.coord(pt(0));
          const b = api.coord(pt(maxD));
          const len = Math.hypot(b[0] - a[0], b[1] - a[1]) || 1;
          const nx = -(b[1] - a[1]) / len, ny = (b[0] - a[0]) / len;
          const pts: Pt[] = segments.map((s) => {
            const m = api.coord(pt((s.depth_start + s.depth_end) / 2));
            const off = BAND.rqdBase + Math.min(1, Math.max(0, s.rqd / 100)) * BAND.rqdSpan;
            return [m[0] + nx * off, m[1] + ny * off];
          });
          const children: object[] = [
            { type: 'polyline', shape: { points: pts }, style: { stroke: '#0f766e', lineWidth: 1.5, fill: 'none' } },
          ];
          for (const q of pts) children.push({ type: 'circle', shape: { cx: q[0], cy: q[1], r: 2 }, style: { fill: '#0f766e' } });
          return { type: 'group', children };
        },
      });
    }

    return {
      animation: false,
      grid: { left: 190, right: 70, top: 34, bottom: 30, containLabel: false },
      xAxis: { type: 'value', min: -2, max: pt(maxD)[0] + 2, show: false },
      yAxis: {
        type: 'value', inverse: true, min: 0, max: pt(maxD)[1] * 1.02,
        name: '垂深(m)', nameTextStyle: { color: '#64748b' },
        axisLabel: { formatter: (v: number) => v.toFixed(0) },
        splitLine: { lineStyle: { color: '#e2e8f0' } },
      },
      tooltip: { trigger: 'item', confine: true },
      series,
    };
  }, [mode, borehole, segments, dip, showInteg, showLith, showRqd]);

  const planOption = useMemo(
    () => (mode === 'plan' ? buildPlanOption(boreholes, project?.cave_axis_azimuth ?? null, borehole?.id ?? null) : null),
    [mode, boreholes, project, borehole],
  );

  const option = mode === 'profile' ? profileOption : planOption;
  const { ref, getChart } = useChart<HTMLDivElement>(option);

  const exportPng = async () => {
    const chart = getChart();
    if (chart == null) return;
    const name = borehole?.number ?? '全项目';
    const path = await saveFileAs(`柱状图_${mode === 'plan' ? '平面' : '剖面'}_${name}.png`, ['png']);
    if (path == null) return;
    await writeFileBytes(path, dataUrlToBytes(chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#ffffff' })));
    toast('success', `已导出 ${path}`);
  };

  if (borehole == null) {
    return <EmptyState title="柱状剖面图" hint="请先在左侧选择钻孔。" />;
  }
  const planEmpty = mode === 'plan' && planOption == null;
  const profileEmpty = mode === 'profile' && profileOption == null;

  return (
    <div className="h-full flex flex-col">
      <div
        className="flex items-center gap-2 px-3 py-2 border-b border-line bg-surface"
        onContextMenu={(e) => {
          e.preventDefault();
          showContextMenu(e.clientX, e.clientY, [
            { label: '剖面图', action: () => setMode('profile') },
            { label: '平面布置图', action: () => setMode('plan') },
            { sep: true },
            { label: '完整性带', hint: showInteg ? '隐藏' : '显示', action: () => setShowInteg((v) => !v) },
            { label: '岩性带', hint: showLith ? '隐藏' : '显示', action: () => setShowLith((v) => !v) },
            { label: 'RQD 曲线', hint: showRqd ? '隐藏' : '显示', action: () => setShowRqd((v) => !v) },
            { sep: true },
            { label: '导出 PNG…', action: () => void exportPng() },
          ]);
        }}
      >
        <Button variant={mode === 'profile' ? 'primary' : 'default'} onClick={() => setMode('profile')}>剖面图</Button>
        <Button variant={mode === 'plan' ? 'primary' : 'default'} onClick={() => setMode('plan')}>平面布置图</Button>
        <span className="mx-1 h-5 w-px bg-line" />
        <label className="flex items-center gap-1 text-xs text-slate-600"><input type="checkbox" checked={showInteg} onChange={() => setShowInteg((v) => !v)} />完整性带</label>
        <label className="flex items-center gap-1 text-xs text-slate-600"><input type="checkbox" checked={showLith} onChange={() => setShowLith((v) => !v)} />岩性带</label>
        <label className="flex items-center gap-1 text-xs text-slate-600"><input type="checkbox" checked={showRqd} onChange={() => setShowRqd((v) => !v)} />RQD 曲线</label>
        <span className="mx-1 h-5 w-px bg-line" />
        <span className="flex items-center gap-1.5 text-xs text-slate-500">
          {([RockClass.I, RockClass.II, RockClass.III, RockClass.IV, RockClass.V] as RockClass[]).map((c) => (
            <span key={c} className="inline-flex items-center gap-0.5">
              <i className="inline-block h-3 w-3 rounded-sm" style={{ background: rockClassColor(c) }} />
              {rockClassNumeral(c)}
            </span>
          ))}
          <span className="inline-flex items-center gap-0.5"><i className="inline-block h-3 w-3 rounded-sm bg-[#9ca3af]" />未分类</span>
        </span>
        <Button className="ml-auto" onClick={() => void exportPng()}>导出 PNG</Button>
      </div>
      <div className="flex-1 min-h-0 relative">
        <div ref={ref} className="w-full h-full" />
        {planEmpty && <div className="absolute inset-0 bg-surface"><EmptyState title="暂无孔口坐标" hint="在钻孔属性里填写 X/Y 坐标后即可查看平面布置图。" /></div>}
        {profileEmpty && <div className="absolute inset-0 bg-surface"><EmptyState title="该孔暂无分段数据" hint="导入 Excel 后自动生成。" /></div>}
      </div>
    </div>
  );
}

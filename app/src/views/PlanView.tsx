/**
 * 平面布置图：孔口 X/Y 散点 + 洞轴线方位（方位角 0=北=+Y，顺时针）。
 * 只导出 buildPlanOption，由 Borehole3DView 的平面模式挂载。
 */

import type { BoreholeRow } from '../db/repos';
import { inclinationToZenith } from '../engine/boreholeGeometry';
import { echarts } from '../lib/echarts';

export function buildPlanOption(
  boreholes: readonly BoreholeRow[],
  caveAxisAzimuth: number | null,
  activeId: number | null,
): echarts.EChartsCoreOption | null {
  const pts = boreholes.filter((b) => b.orifice_x != null && b.orifice_y != null);
  if (pts.length === 0) return null;

  const xs = pts.map((b) => b.orifice_x as number);
  const ys = pts.map((b) => b.orifice_y as number);
  const cx = (Math.min(...xs) + Math.max(...xs)) / 2;
  const cy = (Math.min(...ys) + Math.max(...ys)) / 2;
  const spread = Math.max(Math.max(...xs) - Math.min(...xs), Math.max(...ys) - Math.min(...ys), 10);
  const L = spread * 1.4;

  const series: object[] = [
    {
      type: 'scatter',
      symbolSize: 12,
      data: pts.map((b) => ({
        name: b.number,
        value: [b.orifice_x, b.orifice_y],
        itemStyle: { color: b.id === activeId ? '#2f5b8f' : '#7fa3c9', borderColor: '#1e3a5f', borderWidth: 1 },
        label: {
          show: true, position: 'right', formatter: b.number,
          color: '#334155', fontSize: 11,
        },
      })),
      tooltip: {
        formatter: (p: { dataIndex: number }) => {
          const b = pts[p.dataIndex];
          const rows = [
            `<b>${b.number}</b>`,
            `X=${b.orifice_x}　Y=${b.orifice_y}`,
            `方位角 ${b.azimuth ?? '未填'}°　天顶角 ${b.inclination_angle == null ? '未填' : inclinationToZenith(b.inclination_angle)}°`,
          ];
          if (b.orifice_elevation != null) rows.push(`孔口高程 ${b.orifice_elevation} m`);
          return rows.join('<br/>');
        },
      },
      z: 5,
    },
  ];

  if (caveAxisAzimuth != null) {
    const rad = (caveAxisAzimuth * Math.PI) / 180;
    const dx = Math.sin(rad), dy = Math.cos(rad);
    const p1: [number, number] = [cx - dx * L, cy - dy * L];
    const p2: [number, number] = [cx + dx * L, cy + dy * L];
    series.push({
      type: 'custom',
      coordinateSystem: 'cartesian2d',
      data: [{ value: p2 }],
      silent: true,
      z: 4,
      renderItem: (_p: unknown, api: { coord: (v: number[]) => number[] }) => {
        const a = api.coord(p1);
        const b = api.coord(p2);
        return {
          type: 'group',
          children: [
            { type: 'line', shape: { x1: a[0], y1: a[1], x2: b[0], y2: b[1] }, style: { stroke: '#b91c1c', lineWidth: 1.5, lineDash: [6, 4] } },
            { type: 'text', x: b[0] + 6, y: b[1] - 14, style: { text: `洞轴线 ${caveAxisAzimuth}°`, fill: '#b91c1c', font: 'bold 11px sans-serif' } },
          ],
        };
      },
    });
  }

  return {
    animation: false,
    grid: { left: 70, right: 60, top: 30, bottom: 46, containLabel: true },
    xAxis: {
      type: 'value', name: 'X（东）', scale: true,
      axisLabel: { fontSize: 10 }, nameTextStyle: { color: '#64748b' },
      splitLine: { lineStyle: { color: '#e8edf3' } },
    },
    yAxis: {
      type: 'value', name: 'Y（北）', scale: true,
      axisLabel: { fontSize: 10 }, nameTextStyle: { color: '#64748b' },
      splitLine: { lineStyle: { color: '#e8edf3' } },
    },
    tooltip: { trigger: 'item', confine: true },
    series,
  };
}

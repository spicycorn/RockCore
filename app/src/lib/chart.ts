/**
 * ECharts 通用挂载钩子与导出辅助。
 */

import { useEffect, useRef } from 'react';
import { echarts } from './echarts';

/** 管理一个 echarts 实例：容器尺寸自适应、option 变化时全量刷新 */
export function useChart<T extends HTMLElement>(
  option: echarts.EChartsCoreOption | null,
): { ref: React.RefObject<T | null>; getChart: () => echarts.ECharts | null } {
  const ref = useRef<T | null>(null);
  const instRef = useRef<echarts.ECharts | null>(null);

  useEffect(() => {
    if (ref.current == null) return;
    const chart = echarts.init(ref.current);
    instRef.current = chart;
    const ro = new ResizeObserver(() => chart.resize());
    ro.observe(ref.current);
    return () => {
      ro.disconnect();
      chart.dispose();
      instRef.current = null;
    };
  }, []);

  useEffect(() => {
    if (instRef.current != null && option != null) {
      instRef.current.setOption(option, true);
    }
  }, [option]);

  return { ref, getChart: () => instRef.current };
}

/** dataURL（PNG）→ 字节数组 */
export function dataUrlToBytes(dataUrl: string): Uint8Array {
  const b64 = dataUrl.slice(dataUrl.indexOf(',') + 1);
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

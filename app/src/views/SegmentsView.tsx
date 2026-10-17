/**
 * 分段与围岩分类编辑器：
 *  - 每段录入 F.0.2 六参数（岩质/结构/坚硬/均一/地下水/洞轴夹角），结构下拉按岩质+完整性级联；
 *  - 完整性等级默认自动判级，可逐段覆盖；
 *  - 地下水、洞轴夹角默认「自动」：地下水按 水位埋深+倾角+段底深度 判定，
 *    洞轴夹角按 孔方位角+倾角+洞轴线方位角 计算（<阈值判是）；均可人工覆盖；
 *  - 分类实时重算（纯函数），保存才落库；支持勾选批量应用参数；
 *  - 缺项行高亮并给出可操作提示，绝不伪造结果。
 */

import { useEffect, useMemo, useRef, useState } from 'react';
import clsx from 'clsx';
import { AppDialog, Button, EmptyState, Select, showContextMenu } from '../components/ui';
import { useApp } from '../store/app';
import { finalIntegrityLevel, segmentF02, type SegmentClassificationWrite } from '../db/repos';
import { autoGroundwater, boreholeCaveAngle } from '../engine/boreholeGeometry';
import { availableStructures } from '../engine/config';
import { classifySegments, classificationDisplayText, missingFieldsHint, type SegmentF02 } from '../engine/classify';
import {
  GroundwaterCondition, IntegrityLevel, integrityLevelLabel,
  RockClass, rockClassNumeral, RockHardnessLevel, RockHomogeneity, rockHomogeneityLabel,
  rockHardnessLabel, RockStructureType, rockStructureLabel, RockType, rockTypeLabel,
} from '../engine/enums';
import { rockClassColor, textOn } from '../lib/colors';

/** 单段编辑草稿（undefined = 未改动） */
interface Edit {
  override?: IntegrityLevel | null;
  rockType?: RockType;
  structureType?: RockStructureType;
  hardness?: RockHardnessLevel;
  homogeneity?: RockHomogeneity;
  groundwater?: GroundwaterCondition;
  caveLt30?: boolean | null;
}

const KEEP = '-1';

/** 枚举标签 → 下拉选项；withUnsetLabel 提供 value='0' 的未选档 */
function opts(labels: Record<number, string>, withUnsetLabel?: string): { value: string; label: string }[] {
  const list = (Object.entries(labels) as [string, string][]).map(([k, v]) => ({ value: k, label: v }))
    .filter((o) => o.value !== '0');
  if (withUnsetLabel != null) list.unshift({ value: '0', label: withUnsetLabel });
  return list;
}

export function SegmentsView() {
  const segments = useApp((s) => s.segments);
  const config = useApp((s) => s.config);
  const boreholeId = useApp((s) => s.boreholeId);
  const borehole = useApp((s) => s.boreholes.find((b) => b.id === s.boreholeId) ?? null);
  const project = useApp((s) => s.projects.find((p) => p.id === s.projectId) ?? null);
  const saveSegmentEdits = useApp((s) => s.saveSegmentEdits);
  const toast = useApp((s) => s.toast);
  const setTab = useApp((s) => s.setTab);

  const [edits, setEdits] = useState<Record<number, Edit>>({});
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [onlyMissing, setOnlyMissing] = useState(false);
  const [batchOpen, setBatchOpen] = useState(false);
  const [dirty, setDirty] = useState(false);

  // 切换钻孔时丢弃未保存编辑（编辑态按段 id 存，跨孔无意义）
  const [lastBorehole, setLastBorehole] = useState(boreholeId);
  if (lastBorehole !== boreholeId) {
    setLastBorehole(boreholeId);
    setEdits({}); setSelected(new Set()); setDirty(false);
  }

  /** 洞轴夹角自动值（孔级常量）：缺任一方位/倾角输入 → null（未定） */
  const caveThreshold = config.thresholds.caveAxisAngleThreshold;
  const autoCaveAngle = useMemo(
    () => boreholeCaveAngle(borehole?.azimuth ?? null, borehole?.inclination_angle ?? null, project?.cave_axis_azimuth ?? null),
    [borehole, project],
  );

  /** 生效值 = 草稿覆盖库值；地下水/洞轴夹角选「自动」时按孔参回填 */
  const effective = useMemo(() => segments.map((row) => {
    const e = edits[row.id] ?? {};
    const base = segmentF02(row);
    // 落库存原始覆盖值：地下水 0=自动、洞轴夹角 null=自动
    const gwRaw = e.groundwater ?? base.groundwater;
    const caveRaw = e.caveLt30 !== undefined ? e.caveLt30 : base.caveAxisAngleLessThan30;
    const autoGw = autoGroundwater(row.depth_end, borehole?.inclination_angle ?? null, borehole?.groundwater_depth ?? null);
    const gwEffective = gwRaw !== GroundwaterCondition.NotSet
      ? gwRaw
      : autoGw == null
        ? GroundwaterCondition.NotSet
        : autoGw ? GroundwaterCondition.Wet : GroundwaterCondition.Dry;
    const caveEffective = caveRaw ?? (autoCaveAngle == null ? null : autoCaveAngle < caveThreshold);
    return {
      row,
      gwRaw,
      caveRaw,
      autoGw,
      level: (e.override !== undefined ? e.override : finalIntegrityLevel(row)) as IntegrityLevel,
      f02: {
        rockType: e.rockType ?? base.rockType,
        structureType: e.structureType ?? base.structureType,
        hardnessLevel: e.hardness ?? base.hardnessLevel,
        homogeneity: e.homogeneity ?? base.homogeneity,
        groundwater: gwEffective,
        caveAxisAngleLessThan30: caveEffective,
      } satisfies SegmentF02,
    };
  }), [segments, edits, borehole, autoCaveAngle, caveThreshold]);

  /** 实时分类（纯函数，不落库） */
  const classification = useMemo(() => {
    const classifiable = effective.map((e) => ({
      depthStart: e.row.depth_start,
      depthEnd: e.row.depth_end,
      runs: [{ key: e.row.id }],
    }));
    const levels = new Map(effective.map((e) => [e.row.id, e.level]));
    const f02Map = new Map(effective.map((e) => [e.row.id, e.f02]));
    return classifySegments(classifiable, levels, (seg) => f02Map.get(seg.runs[0].key)!);
  }, [effective]);

  const resultById = useMemo(
    () => new Map(classification.classifications.map((c) => [c.segmentKey, c])),
    [classification],
  );

  const classCounts = useMemo(() => {
    const counts = new Map<RockClass, number>();
    let unclassified = 0;
    for (const c of classification.classifications) {
      if (c.result.success) counts.set(c.result.rockClass, (counts.get(c.result.rockClass) ?? 0) + 1);
      else unclassified++;
    }
    return { counts, unclassified };
  }, [classification]);

  function patch(id: number, patchEdit: Edit): void {
    setEdits((prev) => ({ ...prev, [id]: { ...prev[id], ...patchEdit } }));
    setDirty(true);
  }

  async function save(): Promise<void> {
    const writes: SegmentClassificationWrite[] = effective.map(({ row, f02, gwRaw, caveRaw }) => {
      const c = resultById.get(row.id)!;
      const e = edits[row.id];
      return {
        id: row.id,
        // 草稿明确改过覆盖 → 用草稿（null=恢复自动）；没改过 → 维持库存覆盖
        integrityOverride: e?.override !== undefined ? e.override : row.integrity_override,
        rockType: f02.rockType,
        structureType: f02.structureType,
        hardness: f02.hardnessLevel,
        homogeneity: f02.homogeneity,
        // 落库原始覆盖值（0/null=自动），生效值只进分类不落库
        groundwater: gwRaw,
        caveAngleLt30: caveRaw,
        rockClass: c.result.success ? c.result.rockClass : null,
        basis: c.result.basis,
      };
    });
    await saveSegmentEdits(writes);
    setDirty(false);
    toast('success', '分段参数与分类结果已保存');
  }
  const saveRef = useRef(save);
  saveRef.current = save;

  // Ctrl+S 保存
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        if (dirty) void saveRef.current();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [dirty]);

  const visible = onlyMissing
    ? effective.filter((e) => !resultById.get(e.row.id)?.result.success)
    : effective;

  if (boreholeId == null) {
    return <EmptyState title="请先选择钻孔" hint="在左侧选择或新建一个钻孔后，这里可以录入围岩分类参数。" />;
  }
  if (segments.length === 0) {
    return <EmptyState title="该钻孔还没有分段数据" hint="先导入 Excel 回次数据，软件会自动归并出完整性分段。"
      ><Button variant="primary" onClick={() => setTab('import')}>去导入</Button></EmptyState>;
  }

  return (
    <div className="h-full flex flex-col">
      {/* 工具栏 */}
      <div className="flex items-center gap-2 px-3 py-2 border-b border-line bg-surface">
        <Button variant="primary" disabled={!dirty} title="Ctrl+S" onClick={() => void save()}>保存{dirty ? ' •' : ''}</Button>
        <Button disabled={selected.size === 0} title="把一组参数批量应用到勾选的分段" onClick={() => setBatchOpen(true)}>
          批量应用{selected.size > 0 ? `（${selected.size}）` : ''}
        </Button>
        <label className="flex items-center gap-1 text-[13px] text-slate-600 ml-1">
          <input type="checkbox" checked={onlyMissing} onChange={(e) => setOnlyMissing(e.target.checked)} />
          只看未分类
        </label>
        <div className="ml-auto flex items-center gap-1.5 text-xs">
          {[RockClass.I, RockClass.II, RockClass.III, RockClass.IV, RockClass.V].map((c) =>
            (classCounts.counts.get(c) ?? 0) > 0 ? (
              <span key={c} className="inline-flex items-center gap-1 rounded-full border border-line bg-surface-alt px-2 h-6">
                <i className="size-2.5 rounded-full" style={{ backgroundColor: rockClassColor(c) }} />
                {rockClassNumeral(c)} {classCounts.counts.get(c)}
              </span>
            ) : null,
          )}
          {classCounts.unclassified > 0 && (
            <span className="inline-flex items-center rounded-full border border-amber-300 bg-amber-50 px-2 h-6 text-amber-700">
              未分类 {classCounts.unclassified}
            </span>
          )}
        </div>
      </div>

      {missingFieldsHint(classification) != null && (
        <div className="px-3 py-1.5 bg-amber-50 border-b border-amber-200 text-xs text-amber-800">
          {missingFieldsHint(classification)}补齐后分类实时重算，保存后写入。
        </div>
      )}

      {/* 分段表 */}
      <div className="flex-1 overflow-auto bg-surface">
        <table className="rc-table">
          <thead>
            <tr>
              <th className="w-8">
                <input
                  type="checkbox"
                  checked={visible.length > 0 && visible.every((e) => selected.has(e.row.id))}
                  onChange={(e) => setSelected(e.target.checked ? new Set(visible.map((v) => v.row.id)) : new Set())}
                />
              </th>
              <th>段号</th><th>深度 (m)</th><th className="rc-num">厚(m)</th>
              <th className="rc-num">n</th><th className="rc-num">S(cm)</th><th className="rc-num">RQD(%)</th>
              <th>完整性</th><th>岩质</th><th>结构类型</th><th>坚硬程度</th><th>均一性</th><th>地下水（自动）</th><th>洞轴夹角&lt;30°（自动）</th>
              <th>围岩类别</th>
            </tr>
          </thead>
          <tbody>
            {visible.map(({ row, level, f02, gwRaw, caveRaw, autoGw }) => {
              const c = resultById.get(row.id)!;
              const isDirty = edits[row.id] != null;
              const structOptions = availableStructures(config, f02.rockType, level)
                .map((s) => ({ value: String(s), label: rockStructureLabel[s] }));
              if (f02.structureType !== RockStructureType.NotSet
                && !structOptions.some((o) => o.value === String(f02.structureType))) {
                structOptions.push({ value: String(f02.structureType), label: rockStructureLabel[f02.structureType] });
              }
              return (
                <tr
                  key={row.id}
                  className={clsx(
                    !c.result.success && 'rc-row-warning',
                    isDirty && 'rc-row-selected',
                  )}
                  onContextMenu={(e) => {
                    e.preventDefault();
                    showContextMenu(e.clientX, e.clientY, [
                      {
                        label: '把本段参数应用到全部段',
                        disabled: f02.rockType === RockType.NotSet,
                        action: () => {
                          const all: Record<number, Edit> = {};
                          for (const s of segments) {
                            all[s.id] = {
                              rockType: f02.rockType, structureType: f02.structureType,
                              hardness: f02.hardnessLevel, homogeneity: f02.homogeneity,
                              groundwater: gwRaw, caveLt30: caveRaw,
                            };
                          }
                          setEdits(all); setDirty(true);
                        },
                      },
                      {
                        label: '放弃本段未保存修改',
                        disabled: !isDirty,
                        action: () => {
                          setEdits((prev) => { const next = { ...prev }; delete next[row.id]; return next; });
                        },
                      },
                    ]);
                  }}
                >
                  <td>
                    <input
                      type="checkbox"
                      checked={selected.has(row.id)}
                      onChange={(e) => {
                        const next = new Set(selected);
                        if (e.target.checked) next.add(row.id); else next.delete(row.id);
                        setSelected(next);
                      }}
                    />
                  </td>
                  <td>{row.seq}</td>
                  <td className="rc-num">{row.depth_start.toFixed(2)} – {row.depth_end.toFixed(2)}</td>
                  <td className="rc-num">{row.thickness_m.toFixed(2)}</td>
                  <td className="rc-num">{row.total_pieces}</td>
                  <td className="rc-num">{row.spacing_cm.toFixed(2)}</td>
                  <td className="rc-num">{row.rqd.toFixed(2)}</td>
                  <td>
                    <Select
                      className="h-6 text-xs"
                      value={String(level)}
                      onChange={(e) => patch(row.id, {
                        override: e.target.value === 'auto' ? null : Number(e.target.value) as IntegrityLevel,
                      })}
                      options={[
                        { value: 'auto', label: `自动：${integrityLevelLabel[row.integrity_level as IntegrityLevel]}` },
                        ...([IntegrityLevel.Intact, IntegrityLevel.RelativelyIntact, IntegrityLevel.Poor,
                          IntegrityLevel.RelativelyBroken, IntegrityLevel.Broken] as IntegrityLevel[])
                          .map((l) => ({ value: String(l), label: `覆盖：${integrityLevelLabel[l]}` })),
                      ]}
                    />
                  </td>
                  <td>
                    <Select
                      className={clsx('h-6 text-xs', f02.rockType === RockType.NotSet && 'border-amber-400')}
                      value={String(f02.rockType)}
                      onChange={(e) => patch(row.id, { rockType: Number(e.target.value) as RockType })}
                      options={opts(rockTypeLabel, '未选')}
                    />
                  </td>
                  <td>
                    <Select
                      className={clsx('h-6 text-xs', f02.structureType === RockStructureType.NotSet && 'border-amber-400')}
                      value={String(f02.structureType)}
                      onChange={(e) => patch(row.id, { structureType: Number(e.target.value) as RockStructureType })}
                      options={[{ value: KEEP, label: '未选' }, ...structOptions]}
                    />
                  </td>
                  <td>
                    <Select
                      className={clsx('h-6 text-xs', f02.hardnessLevel === RockHardnessLevel.NotSet && 'border-amber-400')}
                      value={String(f02.hardnessLevel)}
                      onChange={(e) => patch(row.id, { hardness: Number(e.target.value) as RockHardnessLevel })}
                      options={opts(rockHardnessLabel, '未选')}
                    />
                  </td>
                  <td>
                    <Select
                      className={clsx('h-6 text-xs', f02.homogeneity === RockHomogeneity.NotSet && 'border-amber-400')}
                      value={String(f02.homogeneity)}
                      onChange={(e) => patch(row.id, { homogeneity: Number(e.target.value) as RockHomogeneity })}
                      options={opts(rockHomogeneityLabel, '未选')}
                    />
                  </td>
                  <td>
                    <Select
                      className={clsx('h-6 text-xs', f02.groundwater === GroundwaterCondition.NotSet && 'border-amber-400')}
                      value={String(gwRaw)}
                      onChange={(e) => patch(row.id, { groundwater: Number(e.target.value) as GroundwaterCondition })}
                      options={[
                        {
                          value: '0',
                          label: autoGw == null ? '自动（未填水位埋深）' : `自动：${autoGw ? '有水' : '无水'}`,
                        },
                        { value: '1', label: '覆盖：无水' },
                        { value: '3', label: '覆盖：有水' },
                      ]}
                    />
                  </td>
                  <td>
                    <Select
                      className={clsx('h-6 text-xs', f02.caveAxisAngleLessThan30 == null && 'border-amber-400')}
                      value={caveRaw == null ? 'auto' : caveRaw ? 'yes' : 'no'}
                      onChange={(e) => patch(row.id, { caveLt30: e.target.value === 'auto' ? null : e.target.value === 'yes' })}
                      options={[
                        {
                          value: 'auto',
                          label: autoCaveAngle == null
                            ? '自动（缺孔向/洞轴方位）'
                            : `自动：${autoCaveAngle.toFixed(0)}° ${autoCaveAngle < caveThreshold ? '<' + caveThreshold : '≥' + caveThreshold}`,
                        },
                        { value: 'yes', label: `覆盖：<${caveThreshold}°` },
                        { value: 'no', label: `覆盖：≥${caveThreshold}°` },
                      ]}
                    />
                  </td>
                  <td title={c.result.success ? c.result.basis : `未分类：${c.result.basis}`}>
                    {c.result.success ? (
                      <span
                        className="inline-flex items-center rounded px-2 h-[20px] text-[12px] font-semibold"
                        style={{ backgroundColor: rockClassColor(c.result.rockClass), color: textOn(rockClassColor(c.result.rockClass)) }}
                      >
                        {rockClassNumeral(c.result.rockClass)} 类
                      </span>
                    ) : (
                      <span className="text-xs text-amber-700">{classificationDisplayText(c)}</span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <BatchApplyDialog
        open={batchOpen}
        onClose={() => setBatchOpen(false)}
        onApply={(edit) => {
          const next = { ...edits };
          for (const id of selected) next[id] = { ...next[id], ...edit };
          setEdits(next);
          setDirty(true);
          setBatchOpen(false);
          toast('info', `已应用到 ${selected.size} 段（未保存，Ctrl+S 保存）`);
        }}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
// 批量应用
// ---------------------------------------------------------------------------

function BatchApplyDialog({
  open, onClose, onApply,
}: { open: boolean; onClose: () => void; onApply: (edit: Edit) => void }) {
  const [form, setForm] = useState<Record<string, string>>({});
  useEffect(() => { if (open) setForm({}); }, [open]);

  const keep = { value: KEEP, label: '（不变）' };
  const set = (k: string) => (e: React.ChangeEvent<HTMLSelectElement>) => setForm({ ...form, [k]: e.target.value });

  const apply = (): void => {
    const edit: Edit = {};
    if (form.rockType != null && form.rockType !== KEEP) edit.rockType = Number(form.rockType) as RockType;
    if (form.structure != null && form.structure !== KEEP) edit.structureType = Number(form.structure) as RockStructureType;
    if (form.hardness != null && form.hardness !== KEEP) edit.hardness = Number(form.hardness) as RockHardnessLevel;
    if (form.homogeneity != null && form.homogeneity !== KEEP) edit.homogeneity = Number(form.homogeneity) as RockHomogeneity;
    if (form.groundwater != null && form.groundwater !== KEEP) edit.groundwater = Number(form.groundwater) as GroundwaterCondition;
    if (form.cave === 'yes') edit.caveLt30 = true;
    else if (form.cave === 'no') edit.caveLt30 = false;
    else if (form.cave === 'auto') edit.caveLt30 = null;
    onApply(edit);
  };

  return (
    <AppDialog
      open={open}
      onOpenChange={(o) => { if (!o) onClose(); }}
      title="批量应用参数到勾选分段"
      footer={<><Button onClick={onClose}>取消</Button><Button variant="primary" onClick={apply}>应用</Button></>}
    >
      <p className="text-xs text-slate-500 mb-3">只修改选择了具体值的字段，「（不变）」的字段保持各段现状。</p>
      <div className="grid grid-cols-2 gap-3">
        <Select value={form.rockType ?? KEEP} onChange={set('rockType')} options={[keep, ...opts(rockTypeLabel)]} />
        <Select value={form.structure ?? KEEP} onChange={set('structure')} options={[keep, ...opts(rockStructureLabel)]} />
        <Select value={form.hardness ?? KEEP} onChange={set('hardness')} options={[keep, ...opts(rockHardnessLabel)]} />
        <Select value={form.homogeneity ?? KEEP} onChange={set('homogeneity')} options={[keep, ...opts(rockHomogeneityLabel)]} />
        <Select
          value={form.groundwater ?? KEEP}
          onChange={set('groundwater')}
          options={[
            keep,
            { value: '0', label: '地下水：自动（按水位）' },
            { value: '1', label: '地下水：覆盖无水' },
            { value: '3', label: '地下水：覆盖有水' },
          ]}
        />
        <Select
          value={form.cave ?? KEEP}
          onChange={set('cave')}
          options={[
            { value: KEEP, label: '洞轴夹角（不变）' },
            { value: 'auto', label: '洞轴夹角<30°：自动' },
            { value: 'yes', label: '洞轴夹角<30°：覆盖是' },
            { value: 'no', label: '洞轴夹角<30°：覆盖否' },
          ]}
        />
      </div>
    </AppDialog>
  );
}


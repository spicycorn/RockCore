/**
 * 项目 / 钻孔 编辑对话框。
 */

import { useEffect, useState } from 'react';
import { AppDialog, Button, Field, TextInput } from './ui';
import type { BoreholeRow, ProjectRow } from '../db/repos';
import { inclinationToZenith, zenithToInclination } from '../engine/boreholeGeometry';
import { useApp } from '../store/app';

export interface ProjectDraft {
  project_number: string;
  name: string;
  phase: string;
  cave_axis_azimuth: number | null;
}

function toProjectDraft(p?: ProjectRow | null): ProjectDraft {
  return {
    project_number: p?.project_number ?? '',
    name: p?.name ?? '',
    phase: p?.phase ?? '',
    cave_axis_azimuth: p?.cave_axis_azimuth ?? null,
  };
}

export function ProjectDialog({
  open, target, onClose,
}: { open: boolean; target: ProjectRow | null; onClose: () => void }) {
  const saveProject = useApp((s) => s.saveProject);
  const toast = useApp((s) => s.toast);
  const [draft, setDraft] = useState<ProjectDraft>(toProjectDraft());
  useEffect(() => { if (open) setDraft(toProjectDraft(target)); }, [open, target]);

  const num = (v: string): number | null => (v.trim() === '' ? null : Number(v));
  const valid = draft.name.trim().length > 0;

  return (
    <AppDialog
      open={open}
      onOpenChange={(o) => { if (!o) onClose(); }}
      title={target == null ? '新建项目' : '编辑项目'}
      footer={
        <>
          <Button onClick={onClose}>取消</Button>
          <Button
            variant="primary"
            disabled={!valid}
            onClick={async () => {
              await saveProject(target?.id ?? null, { ...draft, name: draft.name.trim() });
              toast('success', target == null ? '项目已创建' : '项目已更新');
              onClose();
            }}
          >
            保存
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-2 gap-3">
        <Field label="项目名称 *">
          <TextInput value={draft.name} autoFocus onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
        </Field>
        <Field label="项目编号">
          <TextInput value={draft.project_number} onChange={(e) => setDraft({ ...draft, project_number: e.target.value })} />
        </Field>
        <Field label="勘察阶段" hint="如：可行性研究 / 初步设计 / 详细勘察">
          <TextInput value={draft.phase} onChange={(e) => setDraft({ ...draft, phase: e.target.value })} />
        </Field>
        <Field label="洞轴线方位角 (°)" hint="用于 F.0.2 中「洞轴线与岩层走向夹角 <30°」判断；可留空，分段编辑里逐段确认">
          <TextInput
            type="number" value={draft.cave_axis_azimuth ?? ''}
            onChange={(e) => setDraft({ ...draft, cave_axis_azimuth: num(e.target.value) })}
          />
        </Field>
      </div>
    </AppDialog>
  );
}

export interface BoreholeDraft {
  number: string;
  orifice_elevation: string;
  /** 孔深：导入后按 Σ回次进尺 自动计算，只读展示 */
  total_depth: string;
  /** 测量深度：仅作核对参考，不参与出图 */
  measured_depth: string;
  groundwater_depth: string;
  azimuth: string;
  /** 天顶角（钻探口径：0=竖直向下，90=水平，>90=仰孔）；库内仍存倾角 */
  zenith_angle: string;
  orifice_x: string;
  orifice_y: string;
  orifice_z: string;
}

const numText = (v: number | null): string => (v == null ? '' : String(v));

function toBoreholeDraft(b?: BoreholeRow | null): BoreholeDraft {
  return {
    number: b?.number ?? '',
    orifice_elevation: numText(b?.orifice_elevation ?? null),
    total_depth: numText(b?.total_depth ?? null),
    measured_depth: numText(b?.measured_depth ?? null),
    groundwater_depth: numText(b?.groundwater_depth ?? null),
    azimuth: numText(b?.azimuth ?? null),
    zenith_angle: numText(b?.inclination_angle == null ? null : inclinationToZenith(b.inclination_angle)),
    orifice_x: numText(b?.orifice_x ?? null),
    orifice_y: numText(b?.orifice_y ?? null),
    orifice_z: numText(b?.orifice_z ?? null),
  };
}

export function BoreholeDialog({
  open, target, onClose,
}: { open: boolean; target: BoreholeRow | null; onClose: () => void }) {
  const saveBorehole = useApp((s) => s.saveBorehole);
  const toast = useApp((s) => s.toast);
  const [draft, setDraft] = useState<BoreholeDraft>(toBoreholeDraft());
  useEffect(() => { if (open) setDraft(toBoreholeDraft(target)); }, [open, target]);

  const num = (v: string): number | null => (v.trim() === '' ? null : Number(v));
  const valid = draft.number.trim().length > 0;
  const set = (k: keyof BoreholeDraft) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDraft({ ...draft, [k]: e.target.value });

  const submit = async () => {
    const zen = num(draft.zenith_angle);
    await saveBorehole(target?.id ?? null, {
      number: draft.number.trim(),
      orifice_elevation: num(draft.orifice_elevation),
      // 孔深由导入按 Σ回次进尺 自动维护；编辑时原样回传，避免被清空
      total_depth: num(draft.total_depth),
      measured_depth: num(draft.measured_depth),
      groundwater_depth: num(draft.groundwater_depth),
      azimuth: num(draft.azimuth),
      inclination_angle: zen == null ? null : zenithToInclination(zen),
      orifice_x: num(draft.orifice_x),
      orifice_y: num(draft.orifice_y),
      orifice_z: num(draft.orifice_z),
    });
    toast('success', target == null ? '钻孔已创建，可以导入数据了' : '钻孔已更新');
    onClose();
  };

  return (
    <AppDialog
      open={open}
      onOpenChange={(o) => { if (!o) onClose(); }}
      title={target == null ? '新建钻孔' : `编辑钻孔 ${target.number}`}
      footer={
        <>
          <Button onClick={onClose}>取消</Button>
          <Button variant="primary" disabled={!valid} onClick={() => void submit()}>
            保存
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-3 gap-3">
        <Field label="孔号 *">
          <TextInput value={draft.number} autoFocus onChange={set('number')} />
        </Field>
        <Field label="孔口高程 (m)">
          <TextInput type="number" value={draft.orifice_elevation} onChange={set('orifice_elevation')} />
        </Field>
        <Field label="孔深 (m)" hint="导入后按 Σ回次进尺 自动计算，出图一律以此为准；不可手填">
          <TextInput type="number" value={draft.total_depth} readOnly title="由导入的回次进尺自动计算"
            onChange={set('total_depth')} className="bg-slate-100 text-slate-500" />
        </Field>
        <Field label="测量深度 (m)" hint="钻具上提丈量值，仅用于与 Σ进尺 核对偏差，不参与出图">
          <TextInput type="number" value={draft.measured_depth} onChange={set('measured_depth')} />
        </Field>
        <Field label="孔向方位角 (°)" hint="用于平面布置图与洞轴线关系">
          <TextInput type="number" value={draft.azimuth} onChange={set('azimuth')} />
        </Field>
        <Field label="天顶角 (°)" hint="钻探口径：0=竖直向下，90=水平，>90=仰孔（0–180）；柱状图按真实孔轴绘制">
          <TextInput type="number" value={draft.zenith_angle} onChange={set('zenith_angle')} />
        </Field>
        <Field label="地下水位埋深 (m)">
          <TextInput type="number" value={draft.groundwater_depth} onChange={set('groundwater_depth')} />
        </Field>
        <Field label="孔口 X">
          <TextInput type="number" value={draft.orifice_x} onChange={set('orifice_x')} />
        </Field>
        <Field label="孔口 Y">
          <TextInput type="number" value={draft.orifice_y} onChange={set('orifice_y')} />
        </Field>
        <Field label="孔口 Z">
          <TextInput type="number" value={draft.orifice_z} onChange={set('orifice_z')} />
        </Field>
      </div>
    </AppDialog>
  );
}

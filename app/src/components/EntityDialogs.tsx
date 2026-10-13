/**
 * 项目 / 钻孔 编辑对话框。
 */

import { useEffect, useState } from 'react';
import { AppDialog, Button, Field, TextInput } from './ui';
import type { BoreholeRow, ProjectRow } from '../db/repos';
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
  total_depth: string;
  groundwater_depth: string;
  azimuth: string;
  inclination_angle: string;
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
    groundwater_depth: numText(b?.groundwater_depth ?? null),
    azimuth: numText(b?.azimuth ?? null),
    inclination_angle: numText(b?.inclination_angle ?? null),
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

  return (
    <AppDialog
      open={open}
      onOpenChange={(o) => { if (!o) onClose(); }}
      title={target == null ? '新建钻孔' : `编辑钻孔 ${target.number}`}
      footer={
        <>
          <Button onClick={onClose}>取消</Button>
          <Button
            variant="primary"
            disabled={!valid}
            onClick={async () => {
              await saveBorehole(target?.id ?? null, {
                number: draft.number.trim(),
                orifice_elevation: num(draft.orifice_elevation),
                total_depth: num(draft.total_depth),
                groundwater_depth: num(draft.groundwater_depth),
                azimuth: num(draft.azimuth),
                inclination_angle: num(draft.inclination_angle),
                orifice_x: num(draft.orifice_x),
                orifice_y: num(draft.orifice_y),
                orifice_z: num(draft.orifice_z),
              });
              toast('success', target == null ? '钻孔已创建，可以导入数据了' : '钻孔已更新');
              onClose();
            }}
          >
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
        <Field label="孔深 (m)">
          <TextInput type="number" value={draft.total_depth} onChange={set('total_depth')} />
        </Field>
        <Field label="孔向方位角 (°)" hint="用于平面布置图与洞轴线关系">
          <TextInput type="number" value={draft.azimuth} onChange={set('azimuth')} />
        </Field>
        <Field label="倾角 (°)" hint="水平=0，竖直=90（负值=仰孔）；柱状剖面按真实倾角绘制">
          <TextInput type="number" value={draft.inclination_angle} onChange={set('inclination_angle')} />
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

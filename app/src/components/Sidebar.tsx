/**
 * 左侧「项目 → 钻孔」树：单击选择、双击编辑、右键菜单（所有操作都有入口）。
 */

import { useState } from 'react';
import clsx from 'clsx';
import { Button, showContextMenu } from './ui';
import { BoreholeDialog, ProjectDialog } from './EntityDialogs';
import { AttachmentDialog } from './AttachmentDialog';
import { startImport } from './ImportFlow';
import { useApp } from '../store/app';
import type { BoreholeRow, ProjectRow } from '../db/repos';

export function Sidebar() {
  const projects = useApp((s) => s.projects);
  const projectId = useApp((s) => s.projectId);
  const boreholes = useApp((s) => s.boreholes);
  const boreholeId = useApp((s) => s.boreholeId);
  const segments = useApp((s) => s.segments);
  const selectProject = useApp((s) => s.selectProject);
  const selectBorehole = useApp((s) => s.selectBorehole);
  const removeProject = useApp((s) => s.removeProject);
  const removeBorehole = useApp((s) => s.removeBorehole);
  const setTab = useApp((s) => s.setTab);

  const [projectDialog, setProjectDialog] = useState<{ open: boolean; target: ProjectRow | null }>({ open: false, target: null });
  const [boreholeDialog, setBoreholeDialog] = useState<{ open: boolean; target: BoreholeRow | null }>({ open: false, target: null });
  const [attachmentDialog, setAttachmentDialog] = useState<{ open: boolean; target: BoreholeRow | null }>({ open: false, target: null });

  const confirmDelete = (what: string, detail: string, onYes: () => void) => {
    // 原生 confirm 在 WebView2 可用且足够醒目；破坏性操作二次确认
    if (window.confirm(`${what}\n${detail}`)) onYes();
  };

  const projectMenu = (e: React.MouseEvent, p: ProjectRow) => {
    e.preventDefault();
    showContextMenu(e.clientX, e.clientY, [
      { label: '新建钻孔', action: () => setBoreholeDialog({ open: true, target: null }) },
      { label: '编辑项目信息', action: () => setProjectDialog({ open: true, target: p }) },
      { sep: true },
      {
        label: '删除项目', danger: true,
        action: () => confirmDelete(`删除项目「${p.name}」？`, '其下全部钻孔、导入数据与分段结果将一并删除，且不可恢复。', () => { void removeProject(p.id); }),
      },
    ]);
  };

  const boreholeMenu = (e: React.MouseEvent, b: BoreholeRow) => {
    e.preventDefault();
    showContextMenu(e.clientX, e.clientY, [
      { label: '导入 Excel 数据…', hint: 'Ctrl+I', action: () => { void selectBorehole(b.id).then(() => startImport()); } },
      { label: '编辑孔属性', action: () => setBoreholeDialog({ open: true, target: b }) },
      { label: '附件（岩心照片等）…', action: () => setAttachmentDialog({ open: true, target: b }) },
      { sep: true },
      { label: '查看回次明细', action: () => { void selectBorehole(b.id).then(() => setTab('runs')); } },
      { label: '分段与围岩分类', action: () => { void selectBorehole(b.id).then(() => setTab('segments')); } },
      { label: '三维模型', action: () => { void selectBorehole(b.id).then(() => setTab('column')); } },
      { sep: true },
      {
        label: '删除钻孔', danger: true,
        action: () => confirmDelete(`删除钻孔「${b.number}」？`, '其导入数据与分段结果将一并删除，且不可恢复。', () => { void removeBorehole(b.id); }),
      },
    ]);
  };

  return (
    <aside className="w-64 shrink-0 border-r border-line bg-surface flex flex-col">
      <div className="h-9 flex items-center justify-between px-2 border-b border-line">
        <span className="text-xs font-semibold text-slate-500 tracking-wide">项目与钻孔</span>
        <Button variant="ghost" className="h-6 px-1.5" title="新建项目" onClick={() => setProjectDialog({ open: true, target: null })}>
          ＋ 项目
        </Button>
      </div>

      <div className="flex-1 overflow-y-auto py-1">
        {projects.length === 0 && (
          <div className="px-3 py-6 text-center text-xs text-slate-400 leading-6">
            还没有项目<br />
            <Button variant="primary" className="mt-2" onClick={() => setProjectDialog({ open: true, target: null })}>新建第一个项目</Button>
          </div>
        )}
        {projects.map((p) => (
          <div key={p.id}>
            <div
              className={clsx(
                'mx-1 rounded px-2 h-8 flex items-center gap-1.5 cursor-pointer select-none',
                projectId === p.id ? 'bg-primary-100 text-primary-700' : 'hover:bg-slate-100 text-slate-700',
              )}
              onClick={() => void selectProject(p.id)}
              onDoubleClick={() => setProjectDialog({ open: true, target: p })}
              onContextMenu={(e) => { void selectProject(p.id); projectMenu(e, p); }}
              title={`${p.name}${p.phase ? `（${p.phase}）` : ''}  双击编辑 / 右键更多`}
            >
              <span className="text-xs">📁</span>
              <span className="truncate text-[13px] font-medium">{p.name}</span>
              {p.phase != null && p.phase !== '' && <span className="ml-auto text-[11px] text-slate-400">{p.phase}</span>}
            </div>
            {projectId === p.id && (
              <div className="mb-1">
                {boreholes.map((b) => (
                  <div
                    key={b.id}
                    className={clsx(
                      'ml-5 mr-1 rounded pl-4 pr-2 h-7 flex items-center gap-1.5 cursor-pointer select-none',
                      boreholeId === b.id ? 'bg-primary-500 text-white' : 'hover:bg-slate-100 text-slate-600',
                    )}
                    onClick={() => void selectBorehole(b.id)}
                    onDoubleClick={() => setBoreholeDialog({ open: true, target: b })}
                    onContextMenu={(e) => { void selectBorehole(b.id); boreholeMenu(e, b); }}
                    title={`孔号 ${b.number}  双击编辑 / 右键更多`}
                  >
                    <span className="text-[11px]">◈</span>
                    <span className="truncate text-[13px]">{b.number}</span>
                    <span className={clsx('ml-auto text-[11px]', boreholeId === b.id ? 'text-white/70' : 'text-slate-400')}>
                      {boreholeId === b.id ? `${segments.length} 段` : b.total_depth != null ? `${b.total_depth}m` : ''}
                    </span>
                  </div>
                ))}
                <button
                  type="button"
                  className="ml-5 my-0.5 rounded px-4 h-6 text-xs text-slate-400 hover:bg-slate-100 hover:text-primary-600"
                  onClick={() => setBoreholeDialog({ open: true, target: null })}
                >
                  ＋ 新建钻孔
                </button>
              </div>
            )}
          </div>
        ))}
      </div>

      <div className="border-t border-line p-2 text-[11px] text-slate-400 leading-5">
        提示：把 .xlsx 拖进窗口任意位置即可导入
      </div>

      <ProjectDialog open={projectDialog.open} target={projectDialog.target} onClose={() => setProjectDialog({ open: false, target: null })} />
      <BoreholeDialog open={boreholeDialog.open} target={boreholeDialog.target} onClose={() => setBoreholeDialog({ open: false, target: null })} />
      <AttachmentDialog open={attachmentDialog.open} borehole={attachmentDialog.target} onClose={() => setAttachmentDialog({ open: false, target: null })} />
    </aside>
  );
}

/**
 * 应用外壳：菜单栏 + 左侧树 + 右侧标签页 + 状态栏。
 * 菜单项与右键、快捷键共用同一批动作。
 */

import { useEffect } from 'react';
import { ContextMenuHost, Tabs, ToastHost, showContextMenu } from './components/ui';
import { Sidebar } from './components/Sidebar';
import { ImportFlowHost, startImport, useGlobalDrop } from './components/ImportFlow';
import { ImportView } from './views/ImportView';
import { RunStatsView } from './views/RunStatsView';
import { SegmentsView } from './views/SegmentsView';
import { ColumnView } from './views/ColumnView';
import { StatsView } from './views/StatsView';
import { SettingsView } from './views/SettingsView';
import { useApp, type TabKey } from './store/app';
import { saveFileAs } from './lib/dialogs';
import { bundledTemplate, writeFileBytes } from './lib/tauri';

export default function App() {
  const ready = useApp((s) => s.ready);
  const initError = useApp((s) => s.initError);
  const init = useApp((s) => s.init);
  const activeTab = useApp((s) => s.activeTab);
  const setTab = useApp((s) => s.setTab);
  const borehole = useApp((s) => s.boreholes.find((b) => b.id === s.boreholeId) ?? null);
  const project = useApp((s) => s.projects.find((p) => p.id === s.projectId) ?? null);
  const busy = useApp((s) => s.busy);
  const toast = useApp((s) => s.toast);

  useEffect(() => { void init(); }, [init]);
  useGlobalDrop();

  // Ctrl+I 导入
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'i') {
        e.preventDefault();
        startImport();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  if (initError != null) {
    return (
      <div className="h-full flex items-center justify-center p-8">
        <div className="max-w-lg rounded-lg border border-red-300 bg-red-50 p-6 text-sm text-red-700 leading-6">
          <div className="font-semibold mb-2">初始化失败</div>
          {initError}
        </div>
      </div>
    );
  }
  if (!ready) {
    return <div className="h-full flex items-center justify-center text-slate-400">正在启动…</div>;
  }

  const fileMenu = (e: React.MouseEvent) => {
    const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
    showContextMenu(rect.left, rect.bottom, [
      { label: '导入 Excel 数据…', hint: 'Ctrl+I', action: () => startImport() },
      {
        label: '下载导入模板…',
        action: () => {
          void (async () => {
            const path = await saveFileAs('岩心回次统计导入模板.xlsx', ['xlsx']);
            if (path == null) return;
            const buf = await bundledTemplate();
            await writeFileBytes(path, new Uint8Array(buf));
            toast('success', `模板已保存到 ${path}`);
          })();
        },
      },
      { sep: true },
      { label: '退出', action: () => window.close() },
    ]);
  };

  const viewMenu = (e: React.MouseEvent) => {
    const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
    showContextMenu(rect.left, rect.bottom, ([
      ['import', '导入'], ['runs', '回次明细'], ['segments', '分段与围岩分类'],
      ['column', '柱状剖面图'], ['stats', '统计'], ['settings', '设置'],
    ] as [TabKey, string][]).map(([key, label]) => ({ label, action: () => setTab(key) })));
  };

  const helpMenu = (e: React.MouseEvent) => {
    const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
    showContextMenu(rect.left, rect.bottom, [
      {
        label: '使用说明',
        action: () => toast('info', '流程：新建项目 → 新建钻孔 → 导入 v10 模板 Excel → 分段编辑里补齐围岩参数 → 查看柱状图与统计。任何操作都有右键/菜单/快捷键入口。'),
      },
      { label: '关于 RockCore', action: () => toast('info', 'RockCore 2.0 — 岩心回次自动分析：RQD · 完整性（表 F.0.4）· 围岩分类（表 F.0.2）') },
    ]);
  };

  return (
    <div className="h-full flex flex-col">
      {/* 菜单栏 */}
      <header className="h-9 shrink-0 flex items-center bg-primary-700 text-white select-none">
        <div className="px-3 font-semibold text-sm tracking-wide">RockCore</div>
        <button type="button" className="px-3 h-full text-[13px] hover:bg-white/10" onClick={fileMenu}>文件</button>
        <button type="button" className="px-3 h-full text-[13px] hover:bg-white/10" onClick={viewMenu}>视图</button>
        <button type="button" className="px-3 h-full text-[13px] hover:bg-white/10" onClick={helpMenu}>帮助</button>
        <div className="ml-auto px-3 text-xs text-white/70">
          {project != null && `${project.name}${project.phase ? `（${project.phase}）` : ''}`}
        </div>
      </header>

      <div className="flex-1 flex min-h-0">
        <Sidebar />
        <main className="flex-1 flex flex-col min-w-0 bg-surface-alt">
          <Tabs<TabKey>
            active={activeTab}
            onChange={setTab}
            items={[
              { key: 'import', label: '导入' },
              { key: 'runs', label: '回次明细' },
              { key: 'segments', label: '分段与围岩分类' },
              { key: 'column', label: '柱状剖面图' },
              { key: 'stats', label: '统计' },
              { key: 'settings', label: '设置' },
            ]}
          />
          <div className="flex-1 min-h-0">
            {activeTab === 'import' && <ImportView />}
            {activeTab === 'runs' && <RunStatsView />}
            {activeTab === 'segments' && <SegmentsView />}
            {activeTab === 'column' && <ColumnView />}
            {activeTab === 'stats' && <StatsView />}
            {activeTab === 'settings' && <SettingsView />}
          </div>
        </main>
      </div>

      {/* 状态栏 */}
      <footer className="h-6 shrink-0 flex items-center gap-4 px-3 bg-surface border-t border-line text-[11px] text-slate-500">
        <span>{borehole != null ? `钻孔 ${borehole.number}` : '未选择钻孔'}</span>
        <span>{busy ?? ''}</span>
        <span className="ml-auto">模板 v10 · 判据 表F.0.4 / 表F.0.2</span>
      </footer>

      <ContextMenuHost />
      <ToastHost />
      <ImportFlowHost />
    </div>
  );
}


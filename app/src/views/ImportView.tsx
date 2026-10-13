/**
 * 导入页：当前钻孔的导入入口（按钮 / 拖拽 / 右键都汇聚到同一流程）与批次信息。
 */

import { Button, EmptyState } from '../components/ui';
import { startImport } from '../components/ImportFlow';
import { useApp } from '../store/app';
import { saveFileAs } from '../lib/dialogs';
import { bundledTemplate, writeFileBytes } from '../lib/tauri';

export function ImportView() {
  const borehole = useApp((s) => s.boreholes.find((b) => b.id === s.boreholeId) ?? null);
  const batches = useApp((s) => s.batches);
  const segments = useApp((s) => s.segments);
  const runStats = useApp((s) => s.runStats);
  const setTab = useApp((s) => s.setTab);
  const toast = useApp((s) => s.toast);

  if (borehole == null) {
    return (
      <EmptyState
        title="欢迎使用 RockCore"
        hint="先在左侧新建项目和钻孔，再把现场记录的 Excel 拖进来——RQD、完整性、围岩分类都由软件按规范自动完成。"
      />
    );
  }

  const latest = batches[0];

  async function downloadTemplate(): Promise<void> {
    const path = await saveFileAs('岩心回次统计导入模板.xlsx', ['xlsx']);
    if (path == null) return;
    const buf = await bundledTemplate();
    await writeFileBytes(path, new Uint8Array(buf));
    toast('success', `模板已保存到 ${path}`);
  }

  return (
    <div className="h-full overflow-auto p-6">
      <div className="mx-auto max-w-3xl flex flex-col gap-4">
        <div className="rounded-lg border border-line bg-surface p-6 shadow-sm">
          <div className="text-base font-semibold text-slate-800 mb-1">
            导入钻孔 {borehole.number} 的回次数据
          </div>
          <p className="text-xs text-slate-500 leading-5 mb-4">
            使用 v10 模板填写：「回次表」记 回次号/进尺(m)，「岩心表」一段一行记 回次号/类型/长度(m)/块数。
            软件会校验闭合、重算 RQD 与平均间距、按表 F.0.4 判完整性、归并完整性分段，并在下一步引导录入围岩分类参数。
          </p>
          <div className="flex items-center gap-2">
            <Button variant="primary" className="h-9 px-4 text-sm" title="Ctrl+I" onClick={() => startImport()}>
              选择 Excel 文件导入…
            </Button>
            <Button className="h-9 px-3" onClick={() => void downloadTemplate()} title="保存随安装包发布的 v10 空白模板">
              下载导入模板
            </Button>
            <span className="text-xs text-slate-400 ml-2">也可以把 .xlsx 直接拖进窗口</span>
          </div>
        </div>

        {latest != null ? (
          <div className="rounded-lg border border-line bg-surface p-4 shadow-sm text-[13px]">
            <div className="font-medium text-slate-700 mb-2">当前数据</div>
            <div className="grid grid-cols-2 gap-x-8 gap-y-1 text-slate-600">
              <span>最近导入：{latest.file_name}</span>
              <span>时间：{latest.imported_at.replace('T', ' ').slice(0, 19)}</span>
              <span>起始深度：{latest.start_depth.toFixed(2)} m</span>
              <span>回次数：{latest.run_count}</span>
              <span>记录段：{runStats.length}</span>
              <span>完整性分段：{segments.length}</span>
            </div>
            <div className="flex gap-2 mt-3">
              <Button onClick={() => setTab('runs')}>查看回次明细</Button>
              <Button onClick={() => setTab('segments')}>分段与围岩分类</Button>
              <Button onClick={() => setTab('column')}>柱状剖面图</Button>
            </div>
          </div>
        ) : (
          <div className="rounded-lg border border-dashed border-line p-6 text-center text-xs text-slate-400 leading-6">
            该钻孔还没有导入过数据。<br />
            没有模板？点上方「下载导入模板」，按现场记录填写后即可导入。
          </div>
        )}
      </div>
    </div>
  );
}

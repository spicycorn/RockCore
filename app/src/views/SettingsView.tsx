/**
 * 设置页：判定阈值 + 表 F.0.4 完整程度划分表编辑。
 * 保存写入 settings 表（settings 键 specification），缺失字段按默认补齐，用户改过的不覆盖。
 */

import { useEffect, useState } from 'react';
import { useApp } from '../store/app';
import {
  defaultIntegrityCriteria, defaultThresholds, ensureDefaults,
  type IntegrityLevelCriterion, type RockSpecificationConfig,
} from '../engine/config';
import { F02_RULES } from '../engine/classificationTable';
import { parseSpacingRange } from '../engine/integrity';
import { Button, Field, Select, TextInput, showContextMenu } from '../components/ui';

const LEVEL_OPTIONS: { value: string; label: string }[] = [
  { value: 'Broken', label: '破碎' },
  { value: 'RelativelyBroken', label: '较破碎' },
  { value: 'Poor', label: '完整性差' },
  { value: 'RelativelyIntact', label: '较完整' },
  { value: 'Intact', label: '完整' },
];

export function SettingsView() {
  const config = useApp((s) => s.config);
  const applyConfig = useApp((s) => s.applyConfig);
  const toast = useApp((s) => s.toast);

  const [draft, setDraft] = useState<RockSpecificationConfig>(config);
  useEffect(() => { setDraft(config); }, [config]);

  const save = () => {
    const bad = draft.integrityLevelCriteria.findIndex((c) => c.jointSpacing.trim() !== '' && parseSpacingRange(c.jointSpacing) == null);
    if (bad >= 0) {
      toast('error', `判定表第 ${bad + 1} 行间距写法无法解析：${draft.integrityLevelCriteria[bad].jointSpacing}`);
      return;
    }
    void applyConfig(ensureDefaults(draft)).then(() => toast('success', '设置已保存。归并阈值变更需重新导入才会重算既有分段。'));
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        save();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  const patchThresholds = (patch: Partial<RockSpecificationConfig['thresholds']>) =>
    setDraft((d) => ({ ...d, thresholds: { ...d.thresholds, ...patch } }));

  const patchCriterion = (i: number, patch: Partial<IntegrityLevelCriterion>) =>
    setDraft((d) => ({
      ...d,
      integrityLevelCriteria: d.integrityLevelCriteria.map((c, j) => (j === i ? { ...c, ...patch } : c)),
    }));

  const moveCriterion = (i: number, dir: -1 | 1) => setDraft((d) => {
    const rows = [...d.integrityLevelCriteria];
    const j = i + dir;
    if (j < 0 || j >= rows.length) return d;
    [rows[i], rows[j]] = [rows[j], rows[i]];
    return { ...d, integrityLevelCriteria: rows };
  });

  const removeCriterion = (i: number) => setDraft((d) => ({
    ...d,
    integrityLevelCriteria: d.integrityLevelCriteria.filter((_, j) => j !== i),
  }));

  const addCriterion = () => setDraft((d) => ({
    ...d,
    integrityLevelCriteria: [...d.integrityLevelCriteria, { levelKey: 'Poor', jointSetCount: '—', jointSpacing: '', jointDevelopment: '' }],
  }));

  const rowMenu = (e: React.MouseEvent, i: number) => {
    e.preventDefault();
    showContextMenu(e.clientX, e.clientY, [
      { label: '上移', action: () => moveCriterion(i, -1) },
      { label: '下移', action: () => moveCriterion(i, 1) },
      { sep: true },
      { label: '删除本行', danger: true, action: () => removeCriterion(i) },
    ]);
  };

  return (
    <div
      className="h-full overflow-auto p-4"
      onContextMenu={(e) => {
        e.preventDefault();
        showContextMenu(e.clientX, e.clientY, [
          { label: '保存设置', hint: 'Ctrl+S', action: save },
          { sep: true },
          { label: '判定表恢复默认', action: () => setDraft((d) => ({ ...d, integrityLevelCriteria: defaultIntegrityCriteria() })) },
          { label: '阈值恢复默认', action: () => setDraft((d) => ({ ...d, thresholds: defaultThresholds() })) },
        ]);
      }}
    >
      <div className="max-w-4xl space-y-6">
        <section className="rounded-lg border border-line bg-surface p-4">
          <h2 className="text-sm font-semibold text-slate-700 mb-3">判定阈值</h2>
          <div className="grid grid-cols-2 gap-4">
            <Field label="洞轴线与岩层走向夹角阈值（°）" hint="表 F.0.2 中「夹角小于该值」的判定，默认 30">
              <TextInput
                type="number" value={draft.thresholds.caveAxisAngleThreshold}
                onChange={(e) => patchThresholds({ caveAxisAngleThreshold: Number(e.target.value) })}
              />
            </Field>
            <Field label="岩体段最小厚度（m）" hint="更薄的段仅在与邻段间距相容时并入，默认 1.0">
              <TextInput
                type="number" step="0.1" value={draft.thresholds.rockSegmentMinThicknessM}
                onChange={(e) => patchThresholds({ rockSegmentMinThicknessM: Number(e.target.value) })}
              />
            </Field>
            <Field label="归并间距容差比例" hint="平均间距相对差超过该比例另起一段，默认 0.5（50%）">
              <TextInput
                type="number" step="0.05" value={draft.thresholds.rockSegmentSpacingToleranceRatio}
                onChange={(e) => patchThresholds({ rockSegmentSpacingToleranceRatio: Number(e.target.value) })}
              />
            </Field>
            <Field label="碎屑平均块径阈值（cm）" hint="间距 ≤10cm 档区分「完整性差/较破碎」：碎屑平均块径（碎屑长÷碎屑块数）低于此值按 >3 组（多组交叉切割）判较破碎，默认 3.3（≈10cm÷3组）">
              <TextInput
                type="number" step="0.1" value={draft.thresholds.fragMeanSizeThresholdCm}
                onChange={(e) => patchThresholds({ fragMeanSizeThresholdCm: Number(e.target.value) })}
              />
            </Field>
            <Field label="相邻回次归并为岩体段" hint="关闭后退回「一个记录段 = 一个完整性分段」">
              <label className="flex items-center gap-2 h-8 text-sm text-slate-700">
                <input
                  type="checkbox" checked={draft.thresholds.mergeRunsIntoRockSegments}
                  onChange={(e) => patchThresholds({ mergeRunsIntoRockSegments: e.target.checked })}
                />
                开启归并（推荐）
              </label>
            </Field>
          </div>
        </section>

        <section className="rounded-lg border border-line bg-surface p-4">
          <div className="flex items-center mb-1">
            <h2 className="text-sm font-semibold text-slate-700">表 F.0.4 完整程度划分</h2>
            <Button className="ml-auto" onClick={addCriterion}>添加行</Button>
            <Button className="ml-2" onClick={() => setDraft((d) => ({ ...d, integrityLevelCriteria: defaultIntegrityCriteria() }))}>恢复默认</Button>
          </div>
          <p className="text-xs text-slate-500 mb-3 leading-5">
            自动判级按「<b>估算组数 + 间距</b>」联合查表（间距区间写法：<code>A~B</code> 下限开上限闭、<code>&gt;N</code>、<code>&lt;N</code>、<code>—</code>=不限）。
            组数由回次数据估算：间距 &gt;50cm→1~2 组；10~50cm→2~3 组；≤10cm 档按碎屑平均块径（碎屑长÷碎屑块数）与上方阈值比较，细碎→&gt;3 组、否则 2~3 组。
            组数写「无序」或间距写「—」的行不参与自动判级（如破碎行，仅可在分段页人工覆盖）。
            行序 = 优先级，自上而下首次匹配即返回；本表修改<b>立即影响导入判级</b>（既有数据需重新导入重算）。
          </p>
          <table className="rc-table w-full text-[13px]">
            <thead>
              <tr>
                <th className="w-10">#</th>
                <th>完整性等级</th>
                <th>结构面间距（cm）*</th>
                <th>组数（参与匹配）</th>
                <th>发育程度（描述）</th>
              </tr>
            </thead>
            <tbody>
              {draft.integrityLevelCriteria.map((c, i) => {
                const bad = c.jointSpacing.trim() !== '' && parseSpacingRange(c.jointSpacing) == null;
                return (
                  <tr key={i} onContextMenu={(e) => rowMenu(e, i)}>
                    <td className="text-slate-400">{i + 1}</td>
                    <td>
                      <Select
                        options={LEVEL_OPTIONS}
                        value={LEVEL_OPTIONS.some((o) => o.value === c.levelKey) ? c.levelKey : 'Poor'}
                        onChange={(e) => patchCriterion(i, { levelKey: e.target.value })}
                      />
                    </td>
                    <td>
                      <TextInput
                        value={c.jointSpacing}
                        className={bad ? 'border-amber-500 bg-amber-50' : ''}
                        placeholder="如 >100 / 30~10 / ≤10 / —"
                        onChange={(e) => patchCriterion(i, { jointSpacing: e.target.value })}
                      />
                    </td>
                    <td><TextInput value={c.jointSetCount} onChange={(e) => patchCriterion(i, { jointSetCount: e.target.value })} /></td>
                    <td><TextInput value={c.jointDevelopment} onChange={(e) => patchCriterion(i, { jointDevelopment: e.target.value })} /></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <p className="text-xs text-slate-400 mt-2">行右键：上移 / 下移 / 删除。</p>
        </section>

        <section className="rounded-lg border border-line bg-surface p-4">
          <h2 className="text-sm font-semibold text-slate-700 mb-1">表 F.0.2 围岩初步分类（规范固定，只读）</h2>
          <p className="text-xs text-slate-500 mb-3 leading-5">
            判定引擎按本表逐条查表（classify），地下水、坚硬程度、均一性、洞轴线夹角为分段页人工补充信息。
          </p>
          <table className="rc-table w-full text-[13px]">
            <thead>
              <tr>
                <th className="w-10">#</th>
                <th className="w-16">岩质</th>
                <th>岩体结构类型</th>
                <th className="w-20">完整程度</th>
                <th>围岩类别</th>
              </tr>
            </thead>
            <tbody>
              {F02_RULES.map((r) => (
                <tr key={r.no}>
                  <td className="text-slate-400">{r.no}</td>
                  <td>{r.rock}</td>
                  <td>{r.structure}</td>
                  <td>{r.integrity}</td>
                  <td className="text-slate-600">{r.result}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>

        <div className="flex items-center gap-2">
          <Button variant="primary" onClick={save}>保存设置</Button>
          <span className="text-xs text-slate-400">Ctrl+S 保存 · 表 F.0.2 为规范固定内容不提供编辑；表 F.0.4 修改即时生效于后续导入</span>
        </div>
      </div>
    </div>
  );
}

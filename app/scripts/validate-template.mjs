// CI 校验：提交的模板 xlsx 必须与 v10 规格一致（结构/表头/下拉/无示例数据）。
// 任何一项不符 → exit 1，阻断发布。
import ExcelJS from 'exceljs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const appDir = dirname(dirname(fileURLToPath(import.meta.url)));
const tplPath = join(appDir, 'src-tauri', 'resources', '岩心回次统计导入模板.xlsx');

const errors = [];
const need = (cond, msg) => { if (!cond) errors.push(msg); };

const wb = new ExcelJS.Workbook();
await wb.xlsx.readFile(tplPath);

const names = wb.worksheets.map((ws) => ws.name.trim());
need(names.includes('填写说明'), '缺少「填写说明」表');
need(names.includes('回次表'), '缺少「回次表」');
need(names.includes('岩心表'), '缺少「岩心表」');
need(!names.includes('数据录入'), '不应包含旧版「数据录入」表');

const text = (v) => (typeof v === 'string' ? v.trim() : v == null ? '' : String(v));

const wsRun = wb.getWorksheet('回次表');
if (wsRun) {
  need(text(wsRun.getCell(1, 1).value).includes('回次号'), '「回次表」A1 应含「回次号」');
  need(text(wsRun.getCell(1, 2).value).includes('进尺'), '「回次表」B1 应含「进尺」');
  let dataRows = 0;
  for (let r = 2; r <= wsRun.actualRowCount; r++) {
    if (wsRun.getCell(r, 1).value != null || wsRun.getCell(r, 2).value != null) dataRows++;
  }
  need(dataRows === 0, `「回次表」含 ${dataRows} 行数据——模板不得携带示例数据`);
}

const wsCore = wb.getWorksheet('岩心表');
if (wsCore) {
  const headers = ['回次号', '类型', '长度', '块数'];
  headers.forEach((h, i) => {
    need(text(wsCore.getCell(1, i + 1).value).includes(h), `「岩心表」第 ${i + 1} 列表头应含「${h}」`);
  });
  let dataRows = 0;
  for (let r = 2; r <= wsCore.actualRowCount; r++) {
    if (wsCore.getCell(r, 1).value != null || wsCore.getCell(r, 2).value != null
      || wsCore.getCell(r, 3).value != null || wsCore.getCell(r, 4).value != null) dataRows++;
  }
  need(dataRows === 0, `「岩心表」含 ${dataRows} 行数据——模板不得携带示例数据`);
  // exceljs 读取时把区间验证展开为逐格 model（B2、B3…），取首尾两格即可确认下拉存在
  const dv = wsCore.dataValidations.model['B2'];
  const dvEnd = wsCore.dataValidations.model['B2001'];
  need(dv != null && dv.type === 'list' && String(dv.formulae?.[0] ?? '').includes('完整')
    && String(dv.formulae?.[0] ?? '').includes('碎屑'), '「岩心表」B 列缺少 完整|碎屑 下拉验证');
  need(dvEnd != null, '「岩心表」B 列下拉验证未覆盖到第 2001 行');
  need(wsCore.getCell(2, 3).numFmt === '0.000', '「岩心表」长度列数值格式应为 0.000');
}

if (errors.length > 0) {
  console.error('模板校验失败：');
  for (const e of errors) console.error(`  ✗ ${e}`);
  process.exit(1);
}
console.log(`模板校验通过：${tplPath}`);

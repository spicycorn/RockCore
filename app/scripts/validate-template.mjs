// CI 校验：提交的模板 xlsx 必须与 v12 规格一致（结构/双行表头/提示行/RQD公式/下拉/联动/无示例数据）。
// 任何一项不符 → exit 1，阻断发布。
import ExcelJS from 'exceljs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const appDir = dirname(dirname(fileURLToPath(import.meta.url)));
const tplPath = join(appDir, 'src-tauri', 'resources', '岩心回次统计导入模板.xlsx');

const FIRST_DATA_ROW = 3;
const LAST_ROW = 2002;
const PIECE_COLS = 20;
const COL_RQD = 3 + PIECE_COLS;   // 23 = W
const COL_ROCK = COL_RQD + 1;     // 24 = X
const COL_STRUCT = COL_RQD + 2;   // 25 = Y
const COL_HARD = COL_RQD + 3;     // 26 = Z
const HINT_MAIN_A = '整数，从1开始，一个回次填一行，按钻进顺序由浅到深';

const errors = [];
const need = (cond, msg) => { if (!cond) errors.push(msg); };

const wb = new ExcelJS.Workbook();
await wb.xlsx.readFile(tplPath);

const names = wb.worksheets.map((ws) => ws.name.trim());
need(names.includes('填写说明'), '缺少「填写说明」表');
need(names.includes('基本信息'), '缺少「基本信息」表');
need(names.includes('岩心记录'), '缺少「岩心记录」主表');
need(names.includes('选项'), '缺少隐藏的「选项」表（结构类型联动下拉数据源）');
need(!names.includes('数据录入'), '不应包含旧版「数据录入」表（v9 及更早）');
need(!names.includes('回次表') && !names.includes('岩心表'), '不应包含旧版「回次表」「岩心表」（v10/v11 长表）');
const wsOpt = wb.getWorksheet('选项');
if (wsOpt) need(wsOpt.state === 'hidden', '「选项」表必须隐藏');

const text = (v) => (typeof v === 'string' ? v.trim() : v == null ? '' : String(v));

const wsInfo = wb.getWorksheet('基本信息');
if (wsInfo) {
  need(text(wsInfo.getCell(1, 1).value).includes('钻孔编号'), '「基本信息」A1 应含「钻孔编号」');
  need(wsInfo.getCell(1, 2).value == null, '「基本信息」B1 是填写格，模板不得预填');
}

const ws = wb.getWorksheet('岩心记录');
if (ws) {
  need(text(ws.getCell(1, 1).value).includes('回次号'), '「岩心记录」A1 应含「回次号」');
  need(text(ws.getCell(1, 2).value).includes('进尺'), '「岩心记录」B1 应含「进尺」');
  for (let i = 0; i < PIECE_COLS; i++) {
    need(text(ws.getCell(1, 3 + i).value) === `岩心段${i + 1}`, `「岩心记录」第 ${3 + i} 列表头应为「岩心段${i + 1}」`);
  }
  need(text(ws.getCell(1, COL_RQD).value).includes('RQD'), '「岩心记录」RQD 列表头缺失');
  need(text(ws.getCell(1, COL_ROCK).value).includes('岩质'), '「岩心记录」岩质列表头缺失');
  need(text(ws.getCell(1, COL_STRUCT).value).includes('结构'), '「岩心记录」岩体结构类型列表头缺失');
  need(text(ws.getCell(1, COL_HARD).value).includes('坚硬'), '「岩心记录」坚硬程度列表头缺失');

  need(text(ws.getCell(2, 1).value) === HINT_MAIN_A, '「岩心记录」A2 提示行文本与引擎 HINT_MAIN_A 不一致');

  // RQD 公式（首尾行抽查）：SUM 忽略文本格 → 碎屑「长度,块数」不进分子
  for (const r of [FIRST_DATA_ROW, LAST_ROW]) {
    const f = ws.getCell(r, COL_RQD).value?.formula;
    need(typeof f === 'string' && f.includes(`SUM($C${r}:$V${r})`) && f.includes('$B' + r),
      `「岩心记录」第 ${r} 行 RQD 列应为 SUM(岩心段区)/进尺 公式`);
  }

  // 无示例数据（RQD 公式列除外）
  let dataRows = 0;
  for (let r = FIRST_DATA_ROW; r <= ws.actualRowCount; r++) {
    let any = false;
    for (let c = 1; c <= COL_HARD; c++) {
      if (c === COL_RQD) continue;
      if (ws.getCell(r, c).value != null) any = true;
    }
    if (any) dataRows++;
  }
  need(dataRows === 0, `「岩心记录」含 ${dataRows} 行数据——模板不得携带示例数据`);

  need(ws.getCell(FIRST_DATA_ROW, 2).numFmt === '0.000', '「岩心记录」进尺列数值格式应为 0.000');

  // exceljs 读取时把区间验证展开为逐格 model（X3、X4…），取首尾两格即可确认下拉覆盖范围
  const dvAt = (ref) => ws.dataValidations.model[ref];
  const dvRock = dvAt(`X${FIRST_DATA_ROW}`);
  need(dvRock != null && dvRock.type === 'list' && String(dvRock.formulae?.[0] ?? '').includes('硬质岩')
    && String(dvRock.formulae?.[0] ?? '').includes('软质岩'), '「岩心记录」岩质列缺少 硬质岩|软质岩 下拉验证');
  need(dvAt(`X${LAST_ROW}`) != null, `「岩心记录」岩质列下拉验证未覆盖到第 ${LAST_ROW} 行`);
  const dvStruct = dvAt(`Y${FIRST_DATA_ROW}`);
  need(dvStruct != null && String(dvStruct.formulae?.[0] ?? '').includes('INDIRECT'), '「岩心记录」结构类型列缺少 INDIRECT 联动下拉');
  const dvHard = dvAt(`Z${FIRST_DATA_ROW}`);
  need(dvHard != null && String(dvHard.formulae?.[0] ?? '').includes('坚硬岩'), '「岩心记录」坚硬程度列缺少坚硬程度下拉验证');
}

if (errors.length > 0) {
  console.error('模板校验失败：');
  for (const e of errors) console.error(`  ✗ ${e}`);
  process.exit(1);
}
console.log(`模板校验通过：${tplPath}`);

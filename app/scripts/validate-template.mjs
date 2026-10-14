// CI 校验：提交的模板 xlsx 必须与 v13 规格一致（表名/第1行钻孔编号/第2行表头/第3行提示/RQD公式/下拉联动/无示例数据）。
// 任何一项不符 → exit 1，阻断发布。
import ExcelJS from 'exceljs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const appDir = dirname(dirname(fileURLToPath(import.meta.url)));
const tplPath = join(appDir, 'src-tauri', 'resources', '岩心回次统计导入模板.xlsx');

const FIRST_DATA_ROW = 4;   // 第 1 行钻孔编号、第 2 行表头、第 3 行提示
const LAST_ROW = 2003;
const COL_RQD = 3;          // C
const COL_ROCK = 4;         // D
const COL_STRUCT = 5;       // E
const COL_HARD = 6;         // F
const FIRST_PIECE_COL = 7;  // G
const PIECE_COLS = 60;      // 岩心段1..60（G..BN）
const LAST_COL = FIRST_PIECE_COL + PIECE_COLS - 1;
const HINT_MAIN_A = '整数，从1开始，一个回次填一行，按钻进顺序由浅到深';

const errors = [];
const need = (cond, msg) => { if (!cond) errors.push(msg); };

const wb = new ExcelJS.Workbook();
await wb.xlsx.readFile(tplPath);

const names = wb.worksheets.map((ws) => ws.name.trim());
need(names.includes('填写说明'), '缺少「填写说明」表');
need(names.includes('岩心记录'), '缺少「岩心记录」主表');
need(names.includes('选项'), '缺少隐藏的「选项」表（结构类型联动下拉数据源）');
// v13 起「基本信息」单独成表 = v12 旧模板，引擎会整表拒收，模板绝不能带
need(!names.includes('基本信息'), '不应包含「基本信息」表（v12 及更早）——v13 钻孔编号填「岩心记录」B1');
need(!names.includes('数据录入'), '不应包含旧版「数据录入」表（v9 及更早）');
need(!names.includes('回次表') && !names.includes('岩心表'), '不应包含旧版「回次表」「岩心表」（v10/v11 长表）');
const wsOpt = wb.getWorksheet('选项');
if (wsOpt) need(wsOpt.state === 'hidden', '「选项」表必须隐藏');

const text = (v) => (typeof v === 'string' ? v.trim() : v == null ? '' : String(v));

const ws = wb.getWorksheet('岩心记录');
if (ws) {
  // 第 1 行：A1 标签、B1 钻孔编号填写格（模板不得预填）
  need(text(ws.getCell(1, 1).value) === '钻孔编号', '「岩心记录」A1 应为「钻孔编号」标签');
  need(ws.getCell(1, 2).value == null, '「岩心记录」B1 是钻孔编号填写格，模板不得预填');

  // 第 2 行表头（列序 = 引擎常量：回次号|回次进尺|RQD|岩质|结构|坚硬|岩心段1..60）
  const h2 = (c) => text(ws.getCell(2, c).value);
  need(h2(1) === '回次号', `「岩心记录」A2 表头应为「回次号」，实际「${h2(1)}」`);
  need(h2(2).includes('进尺'), '「岩心记录」B2 表头应含「进尺」');
  need(h2(COL_RQD).includes('RQD'), '「岩心记录」C2 表头应含「RQD」');
  need(h2(COL_ROCK).includes('岩质'), '「岩心记录」D2 表头应含「岩质」');
  need(h2(COL_STRUCT).includes('结构'), '「岩心记录」E2 表头应含「岩体结构类型」');
  need(h2(COL_HARD).includes('坚硬'), '「岩心记录」F2 表头应含「坚硬程度」');
  need(h2(FIRST_PIECE_COL) === '岩心段1', `「岩心记录」G2 表头应为「岩心段1」，实际「${h2(FIRST_PIECE_COL)}」`);
  for (let i = 0; i < PIECE_COLS; i++) {
    need(h2(FIRST_PIECE_COL + i) === `岩心段${i + 1}`,
      `「岩心记录」第 ${FIRST_PIECE_COL + i} 列表头应为「岩心段${i + 1}」`);
  }

  // 第 3 行提示：A 列必须与引擎 HINT_MAIN_A 逐字一致（引擎按原文整行跳过）
  need(text(ws.getCell(3, 1).value) === HINT_MAIN_A, '「岩心记录」A3 提示行文本与引擎 HINT_MAIN_A 不一致');

  // RQD 公式（首尾行抽查）：SUMIF 数字判据 → 「长度，破碎」文本格不进分子
  for (const r of [FIRST_DATA_ROW, LAST_ROW]) {
    const f = ws.getCell(r, COL_RQD).value?.formula;
    need(typeof f === 'string' && f.includes('SUMIF') && f.includes('">=0.1"')
      && f.includes(`$G${r}`) && f.includes(`$B${r}`),
      `「岩心记录」第 ${r} 行 RQD 列应为 SUMIF($G${r}:$XFD${r},">=0.1")/进尺 公式`);
  }

  // 数值格式：回次号整数、进尺/RQD/岩心段两位小数
  need(ws.getCell(FIRST_DATA_ROW, 1).numFmt === '0', '「岩心记录」回次号列数值格式应为 0');
  need(ws.getCell(FIRST_DATA_ROW, 2).numFmt === '0.00', '「岩心记录」进尺列数值格式应为 0.00');
  need(ws.getCell(FIRST_DATA_ROW, FIRST_PIECE_COL).numFmt === '0.00', '「岩心记录」岩心段列数值格式应为 0.00');

  // exceljs 读取时把区间验证展开为逐格 model（D4、D5…），取首尾两格即可确认下拉覆盖范围
  const dvAt = (ref) => ws.dataValidations.model[ref];
  const dvRock = dvAt(`D${FIRST_DATA_ROW}`);
  need(dvRock != null && dvRock.type === 'list'
    && String(dvRock.formulae?.[0] ?? '').includes('硬质岩')
    && String(dvRock.formulae?.[0] ?? '').includes('软质岩'), '「岩心记录」岩质列缺少 硬质岩|软质岩 下拉验证');
  const dvStruct = dvAt(`E${FIRST_DATA_ROW}`);
  const structFormula = String(dvStruct?.formulae?.[0] ?? '');
  need(dvStruct != null && structFormula.includes('INDIRECT') && structFormula.includes('$D4'),
    '「岩心记录」结构类型列缺少 INDIRECT 联动下拉（驱动格应为 $D4）');
  const dvHard = dvAt(`F${FIRST_DATA_ROW}`);
  need(dvHard != null && String(dvHard.formulae?.[0] ?? '').includes('坚硬岩'),
    '「岩心记录」坚硬程度列缺少坚硬程度下拉验证');
  for (const col of ['D', 'E', 'F']) {
    need(dvAt(`${col}${LAST_ROW}`) != null, `「岩心记录」${col} 列下拉验证未覆盖到第 ${LAST_ROW} 行`);
  }

  // 无示例数据（RQD 公式列 C 除外）
  let dataRows = 0;
  for (let r = FIRST_DATA_ROW; r <= ws.actualRowCount; r++) {
    let any = false;
    for (let c = 1; c <= LAST_COL; c++) {
      if (c === COL_RQD) continue;
      if (ws.getCell(r, c).value != null) any = true;
    }
    if (any) dataRows++;
  }
  need(dataRows === 0, `「岩心记录」含 ${dataRows} 行数据——模板不得携带示例数据`);
}

if (errors.length > 0) {
  console.error('模板校验失败：');
  for (const e of errors) console.error(`  ✗ ${e}`);
  process.exit(1);
}
console.log(`模板校验通过：${tplPath}`);

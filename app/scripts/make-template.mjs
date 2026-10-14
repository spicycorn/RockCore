// 生成 RockCore 2.0 导入模板（v13 一行一回次，岩心段不限列数）→ app/src-tauri/resources/岩心回次统计导入模板.xlsx
//
// 设计约束（历史教训，勿改）：
//  · 无示例数据行——示例数据会被静默继承进真实项目（v4.1 教训）；
//  · 数值格一律纯数字、单位统一米，表头带单位；
//  · 版本拒收由导入引擎负责（表头 + 「数据录入」「回次表」「岩心表」「基本信息」旧表探测），模板不放版本格；
//  · RQD 列是现场参考公式（SUMIF 数字判据天然排除「长度，破碎」文本格），软件导入一律重算；
//  · 全簿不用黄色填充（v13 起录入格不再黄底，白底细边框即可）。
// v13 要点（与用户逐条敲定）：
//  · 一行一回次：岩心段从 G 列起一段一格、列数不限（模板预铺 60 列 G..BN）——纯数字 = 正常段
//    （短于10厘米的段也直接填数字）；「长度，破碎」（如 1.8，破碎）= 压碎到无法逐块记录的破碎段；
//    旧「长度,块数」碎屑写法已取消；
//  · 第 1 行 A1=「钻孔编号」标签、B1=孔号（一个文件一个孔）；第 2 行表头；第 3 行白底灰斜体填写提示，
//    提示文本 A 列必须与 excelImport.ts 的 HINT_MAIN_A 逐字一致（引擎按原文整行跳过）；数据从第 4 行起；
//  · RQD 移到 C 列，岩质/岩体结构类型/坚硬程度移到 D..F 列（选填三列，下拉选项与 enums.ts 中文标签
//    逐字一致；结构类型用 INDIRECT 联动（选岩质后只出对应半区），选项清单放隐藏「选项」表）。
import ExcelJS from 'exceljs';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const appDir = dirname(dirname(fileURLToPath(import.meta.url)));
const outPath = join(appDir, 'src-tauri', 'resources', '岩心回次统计导入模板.xlsx');
mkdirSync(dirname(outPath), { recursive: true });

const PREPARED_ROWS = 2000; // 预格式化行数（只设格式/公式，不写数据值）
const FIRST_DATA_ROW = 4;   // 第 1 行钻孔编号、第 2 行表头、第 3 行提示
const LAST_ROW = FIRST_DATA_ROW + PREPARED_ROWS - 1; // 2003

const COL_RQD = 3;          // C
const COL_ROCK = 4;         // D
const COL_STRUCT = 5;       // E
const COL_HARD = 6;         // F
const FIRST_PIECE_COL = 7;  // G
const PIECE_COLS = 60;      // 岩心段1..60（G..BN）；引擎不限列数，预铺只是给个格子
const LAST_COL = FIRST_PIECE_COL + PIECE_COLS - 1; // 66 = BN

// —— 与 excelImport.ts 逐字一致的提示行 A 列文本 ——
const HINT_MAIN_A = '整数，从1开始，一个回次填一行，按钻进顺序由浅到深';

// —— 与 enums.ts 中文标签逐字一致的下拉选项 ——
const HARD_STRUCTS = [
  '整体状或巨厚层状结构', '块状结构', '次块状结构', '厚层状或中厚层状结构', '互层状结构',
  '薄层状结构', '镶嵌结构', '块裂结构', '碎裂结构', '碎块状或碎屑状结构',
];
const SOFT_STRUCTS = [
  '整体状或巨厚层状结构', '块状或次块状结构', '厚层、中厚层或互层状结构',
  '薄层状或块裂结构', '碎裂结构', '碎块状或碎屑状散体结构',
];
const ALL_STRUCTS = [...new Set([...HARD_STRUCTS, ...SOFT_STRUCTS])]; // 14 项

const colLetter = (n) => { // 1→A … 26→Z
  let s = '';
  while (n > 0) {
    const m = (n - 1) % 26;
    s = String.fromCharCode(65 + m) + s;
    n = Math.floor((n - 1) / 26);
  }
  return s;
};
const RQD_COL = colLetter(COL_RQD);     // C
const ROCK_COL = colLetter(COL_ROCK);   // D
const STRUCT_COL = colLetter(COL_STRUCT); // E
const HARD_COL = colLetter(COL_HARD);   // F
const FIRST_PIECE = colLetter(FIRST_PIECE_COL); // G

const wb = new ExcelJS.Workbook();
wb.creator = 'RockCore';
wb.lastModifiedBy = 'RockCore';

const thinBorder = {
  top: { style: 'thin' }, left: { style: 'thin' },
  bottom: { style: 'thin' }, right: { style: 'thin' },
};

// 第 2 行表头：加粗 + 浅灰底（v13 起不再深蓝底白字，全簿无黄色填充）
const headerStyle = {
  font: { bold: true },
  fill: { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFD9D9D9' } },
  alignment: { horizontal: 'center', vertical: 'middle', wrapText: true },
  border: thinBorder,
};

// 第 3 行提示：白底（不设 fill）灰字斜体
const hintFont = { color: { argb: 'FF808080' }, italic: true, size: 10 };

// ---------------------------------------------------------------------------
// 填写说明
// ---------------------------------------------------------------------------
const guide = wb.addWorksheet('填写说明');
guide.getColumn(1).width = 100;
const lines = [
  ['RockCore 岩心回次统计导入模板（v13）'],
  [''],
  ['一、填之前先看'],
  ['  · 第 1 行：B1 填「钻孔编号」（本文件对应的钻孔）；一个文件只记一个钻孔，编号建议与软件里的钻孔名一致。'],
  ['  · 第 2 行是表头，不要改；第 3 行灰字是填写提示，不要删，也不要往里写数据。'],
  ['  · 第 4 行起一个回次填一行，按钻进顺序由浅到深排列。'],
  ['  · 所有长度都用米（m）：1.5 就是1米5，0.08 就是8厘米。'],
  [''],
  ['二、「岩心记录」怎么填（一个回次一行）'],
  ['  · 回次号按钻进顺序由浅到深编 1、2、3…连续编号；回次进尺 = 这一回次钻进的米数。'],
  ['  · 岩心段从 G 列起一段一格，按由浅到深依次填，不限列数；中间空格直接跳过（顺序不断）。'],
  ['  · 正常段：直接填长度（米），短于10厘米的段也直接填数字（如 0.08）。'],
  ['  · 破碎段（压碎到无法逐块记录）：填「长度，破碎」。例：1.8，破碎'],
  ['    逗号用英文或中文都行；长度可带单位 m（如 1.8m，破碎）。'],
  ['  · 旧「长度,块数」写法已取消：所有岩心段逐段只填长度，不再填块数。'],
  ['  · RQD(%) 列自动计算（只统计≥10厘米的段），仅供现场参考、不要改公式；软件导入一律重算，以软件为准。'],
  ['  · D/E/F 三列（岩质/岩体结构类型/坚硬程度）选填，只认下拉值；先选「岩质」，结构类型下拉只出对应选项；'],
  ['    不填也能正常导入，回头在软件里补。'],
  [''],
  ['三、闭合校核（软件逐回次自动检查）'],
  ['  · 同一回次所有岩心段长度加起来（含破碎段），不应超过回次进尺（允许差1厘米）。'],
  ['  · 多记 → 导入被拦下；少记属正常取芯损耗，按取芯率记录，不用改。'],
  [''],
  ['四、常见错误'],
  ['  · 把厘米当米填（120 应为 1.2）——软件发现平均间距异常会提醒。'],
  ['  · 还在写「0.08,3」这类旧版碎屑格式——v13 起直接填 0.08。'],
  ['  · 破碎段只写「破碎」没写长度——要写成 1.8，破碎。'],
  ['  · 回次号重复或跳号——软件会逐行报错，按提示改。'],
];
lines.forEach((l, i) => {
  const cell = guide.getCell(i + 1, 1);
  cell.value = l[0];
  cell.font = i === 0 ? { bold: true, size: 14 } : {};
  cell.alignment = { wrapText: true };
});

// ---------------------------------------------------------------------------
// 岩心记录（主表：第 1 行孔号、第 2 行表头、第 3 行提示、第 4 行起数据）
// ---------------------------------------------------------------------------
const ws = wb.addWorksheet('岩心记录');
ws.views = [{ state: 'frozen', xSplit: 0, ySplit: 3 }]; // 冻结至 A4

// 第 1 行：A1 标签 + B1 填写格（加粗、白底——全簿无黄色填充）
ws.getCell(1, 1).value = '钻孔编号';
ws.getCell(1, 1).font = { bold: true };
ws.getCell(1, 2).font = { bold: true };

// 列宽 + 列级数值格式（必须在创建数据格之前设置：exceljs 建格时合并列样式，
// 之后逐格只加 border 不会丢掉继承来的 numFmt，文件里样式表也保持小巧）
ws.getColumn(1).width = 8;   ws.getColumn(1).numFmt = '0';
ws.getColumn(2).width = 12;  ws.getColumn(2).numFmt = '0.00';
ws.getColumn(3).width = 10;  ws.getColumn(3).numFmt = '0.00';
ws.getColumn(4).width = 10;
ws.getColumn(5).width = 14;
ws.getColumn(6).width = 10;
for (let c = FIRST_PIECE_COL; c <= LAST_COL; c++) {
  ws.getColumn(c).width = 9;
  ws.getColumn(c).numFmt = '0.00';
}

// 第 2 行表头
const headers = ['回次号', '回次进尺(m)', 'RQD(%)', '岩质', '岩体结构类型', '坚硬程度',
  ...Array.from({ length: PIECE_COLS }, (_, i) => `岩心段${i + 1}`)];
headers.forEach((h, ci) => {
  const cell = ws.getCell(2, ci + 1);
  cell.value = h;
  cell.style = headerStyle;
});

// 第 3 行提示（白底灰斜体；A 列文本与引擎 HINT_MAIN_A 逐字一致）
const hints = new Array(headers.length).fill('');
hints[0] = HINT_MAIN_A;
hints[1] = 'm，保留两位小数';
hints[2] = '自动计算：只统计≥10cm的段；导入后以软件重算为准';
hints[3] = '选填，下拉选';
hints[4] = '选填，先选岩质';
hints[5] = '选填，下拉选';
hints[6] = '每段一格，单位m，保留两位小数；压碎到无法逐块记录的破碎段填「长度，破碎」（如 1.8，破碎）';
hints.forEach((t, ci) => {
  const cell = ws.getCell(3, ci + 1);
  if (t) cell.value = t;
  cell.font = hintFont;
});
ws.getRow(3).height = 30;

// 预格式化数据区：A..BN 细边框（numFmt 走列级）+ C 列 RQD 公式。
// SUMIF 数字判据 ">=0.1" 只累加数字格，「1.8，破碎」一类文本格天然不进分子。
for (let r = FIRST_DATA_ROW; r <= LAST_ROW; r++) {
  for (let c = 1; c <= LAST_COL; c++) {
    ws.getCell(r, c).border = thinBorder;
  }
  ws.getCell(r, COL_RQD).value = {
    formula: `IF(OR($B${r}="",$B${r}=0),"",ROUND(SUMIF($G${r}:$XFD${r},">=0.1")/$B${r}*100,2))`,
  };
}

// 数据验证下拉（选填三列 D/E/F）。注意：exceljs 字段名是 formulae（不是 formulas，写错会静默丢下拉）
const dv = (ref, formulae, error) => {
  ws.dataValidations.add(ref, {
    type: 'list', allowBlank: true, formulae,
    errorStyle: 'error', errorTitle: '填写有误', error,
    showErrorMessage: true,
  });
};
dv(`${ROCK_COL}${FIRST_DATA_ROW}:${ROCK_COL}${LAST_ROW}`, ['"硬质岩,软质岩"'],
  '岩质只能选「硬质岩」或「软质岩」');
dv(`${STRUCT_COL}${FIRST_DATA_ROW}:${STRUCT_COL}${LAST_ROW}`,
  [`INDIRECT(IF($${ROCK_COL}${FIRST_DATA_ROW}="硬质岩","选项!$B$3:$B$12",IF($${ROCK_COL}${FIRST_DATA_ROW}="软质岩","选项!$C$3:$C$8","选项!$D$3:$D$16")))`],
  '请从下拉选择结构类型（先选岩质可只看对应选项）');
dv(`${HARD_COL}${FIRST_DATA_ROW}:${HARD_COL}${LAST_ROW}`, ['"坚硬岩,中硬岩,较软岩,软岩"'],
  '坚硬程度只能选：坚硬岩/中硬岩/较软岩/软岩');

// ---------------------------------------------------------------------------
// 选项（隐藏表：结构类型 INDIRECT 联动的数据源；内容与 v12 逐字一致）
// ---------------------------------------------------------------------------
const wsOpt = wb.addWorksheet('选项', { state: 'hidden' });
wsOpt.getCell(2, 2).value = '硬岩结构类型';
wsOpt.getCell(2, 3).value = '软岩结构类型';
wsOpt.getCell(2, 4).value = '全部结构类型';
HARD_STRUCTS.forEach((v, i) => { wsOpt.getCell(3 + i, 2).value = v; });
SOFT_STRUCTS.forEach((v, i) => { wsOpt.getCell(3 + i, 3).value = v; });
ALL_STRUCTS.forEach((v, i) => { wsOpt.getCell(3 + i, 4).value = v; });

await wb.xlsx.writeFile(outPath);
console.log(`模板已生成：${outPath}`);

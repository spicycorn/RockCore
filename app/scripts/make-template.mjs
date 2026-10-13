// 生成 RockCore 2.0 导入模板（v12 一行一回次）→ app/src-tauri/resources/岩心回次统计导入模板.xlsx
//
// 设计约束（历史教训，勿改）：
//  · 无示例数据行——示例数据会被静默继承进真实项目（v4.1 教训）；
//  · 数值格一律纯数字、单位统一米，表头带单位；
//  · 版本拒收由导入引擎负责（表头 + 「数据录入」「回次表」「岩心表」旧表探测），模板不放版本格；
//  · RQD 列是现场参考公式（SUM 忽略文本格，碎屑「长度,块数」天然不进分子），软件导入一律重算。
// v12 要点（与用户逐条敲定）：
//  · 一行一回次：岩心段1..20 按由浅到深同列同序——纯数字 = ≥0.1m 整段；「长度,块数」（如 0.08,3）= <10cm 破碎带；
//    碎屑与完整段保持顺序连贯（出岩心柱状图依赖此顺序）；
//  · 双行表头：第 2 行黄底填写提示（手机上也能直接看到），数据从第 3 行起；
//    提示文本 A 列必须与 excelImport.ts 的 HINT_MAIN_A 逐字一致（引擎按原文整行跳过）；
//  · 「基本信息」B1 = 钻孔编号（一个文件一个孔）；
//  · 行尾选填三列：岩质 / 岩体结构类型 / 坚硬程度，下拉选项与 enums.ts 中文标签逐字一致；
//    结构类型用 INDIRECT 联动（选岩质后只出对应半区），选项清单放隐藏「选项」表。
import ExcelJS from 'exceljs';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const appDir = dirname(dirname(fileURLToPath(import.meta.url)));
const outPath = join(appDir, 'src-tauri', 'resources', '岩心回次统计导入模板.xlsx');
mkdirSync(dirname(outPath), { recursive: true });

const PREPARED_ROWS = 2000; // 预格式化行数（只设格式/公式，不写数据值）
const FIRST_DATA_ROW = 3;   // 第 1 行表头、第 2 行提示
const LAST_ROW = FIRST_DATA_ROW + PREPARED_ROWS - 1;

const PIECE_COLS = 20;      // 岩心段1..20（C..V）
const COL_RQD = 3 + PIECE_COLS;      // 23 = W
const COL_ROCK = COL_RQD + 1;        // 24 = X
const COL_STRUCT = COL_RQD + 2;      // 25 = Y
const COL_HARD = COL_RQD + 3;        // 26 = Z

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
const RQD_COL = colLetter(COL_RQD);
const ROCK_COL = colLetter(COL_ROCK);
const STRUCT_COL = colLetter(COL_STRUCT);
const HARD_COL = colLetter(COL_HARD);
const FIRST_PIECE = colLetter(3);   // C
const LAST_PIECE = colLetter(2 + PIECE_COLS); // V

const wb = new ExcelJS.Workbook();
wb.creator = 'RockCore';
wb.lastModifiedBy = 'RockCore';

const headerStyle = {
  font: { bold: true, color: { argb: 'FFFFFFFF' } },
  fill: { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF2F5B8F' } },
  alignment: { horizontal: 'center', vertical: 'middle', wrapText: true },
  border: {
    top: { style: 'thin' }, left: { style: 'thin' },
    bottom: { style: 'thin' }, right: { style: 'thin' },
  },
};

const hintStyle = {
  font: { color: { argb: 'FF7F6000' }, size: 10 },
  fill: { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFFFF2CC' } },
  alignment: { wrapText: true, vertical: 'top' },
  border: {
    top: { style: 'thin' }, left: { style: 'thin' },
    bottom: { style: 'thin' }, right: { style: 'thin' },
  },
};

const inputStyle = {
  fill: { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFFFF2CC' } },
  border: {
    top: { style: 'medium' }, left: { style: 'medium' },
    bottom: { style: 'medium' }, right: { style: 'medium' },
  },
};

// ---------------------------------------------------------------------------
// 填写说明
// ---------------------------------------------------------------------------
const guide = wb.addWorksheet('填写说明');
guide.getColumn(1).width = 100;
const lines = [
  ['RockCore 岩心回次统计导入模板（v12）'],
  [''],
  ['一、填之前先看'],
  ['  · 先在「基本信息」表填「钻孔编号」：一个文件只记一个钻孔。'],
  ['  · 所有长度都用米（m）：1.5 就是1米5，0.08 就是8厘米；格子里只填数字和逗号，不要带单位和别的字。'],
  ['  · 第 2 行的黄底文字是填写提示：不要删掉它，也不要往里面写数据；数据从第 3 行开始填。'],
  [''],
  ['二、「岩心记录」怎么填（一个回次一行）'],
  ['  · 回次号按钻进顺序由浅到深编 1、2、3…连续编号；回次进尺 = 这一回次钻进的米数。'],
  ['  · 岩心段1、岩心段2…按由浅到深一段一格依次填，中间空格直接跳过（顺序不断）。'],
  ['  · 整段（单块10厘米及以上）：只填长度。例：1.2'],
  ['  · 破碎带（不足10厘米的碎块、碎屑、岩粉）：填 长度,块数。例：0.08,3 表示这段破碎带共8厘米、约3块。'],
  ['    逗号用英文或中文都行；块数不填（写成 0.08,）按1块计。'],
  ['  · 一段里有多段破碎带就各占一格，和整段穿插着按深浅排，出岩心图靠这个顺序。'],
  ['  · 一个回次最多20段；真超了就合并相邻小段记录。'],
  ['  · RQD(%) 列自动计算（只统计整段，破碎带不进分子），不要改公式；软件导入时会重算，以软件为准。'],
  ['  · 最后三列（岩质/岩体结构类型/坚硬程度）是选填：现场顺手填最好；不填也能正常导入，回头在软件里补。'],
  ['    先选「岩质」，「岩体结构类型」下拉就只出硬岩（或软岩）对应的结构类型；坚硬程度凭敲锤手感从硬到软选四档。'],
  [''],
  ['三、闭合校核（软件逐回次自动检查）'],
  ['  · 同一回次所有岩心段长度加起来（整段+破碎带），应等于回次进尺（允许差1厘米）。'],
  ['  · 多记 → 导入被拦下；少记 → 给警告，差额算在回次底部（磨损/漏记）。'],
  [''],
  ['四、完整例子（只看格式，别抄进数据表）'],
  ['  回次号=1  进尺=2.0  岩心段1=1.2  岩心段2=0.7  岩心段3=0.08,3 → RQD=(1.2+0.7)/2.0×100=95'],
  ['  回次号=2  进尺=1.5  岩心段1=0.05,6  岩心段2=1.4  → 破碎带在前（浅），整段在后（深），顺序照实填'],
  [''],
  ['五、常见错误'],
  ['  · 把厘米当米填（120 应为 1.2）——软件发现平均间距异常会提醒。'],
  ['  · 不足10厘米的段只填了长度（如 0.05）——要写成 0.05,块数，否则导入被拦。'],
  ['  · 回次号重复或跳号——软件会逐行报错，按提示改。'],
];
lines.forEach((l, i) => {
  const cell = guide.getCell(i + 1, 1);
  cell.value = l[0];
  cell.font = i === 0 ? { bold: true, size: 14 } : {};
  cell.alignment = { wrapText: true };
});

// ---------------------------------------------------------------------------
// 岩心记录（主表：一行一回次）
// ---------------------------------------------------------------------------
const ws = wb.addWorksheet('岩心记录');
ws.views = [{ state: 'frozen', xSplit: 2, ySplit: 2 }];

const headers = ['回次号', '回次进尺(m)',
  ...Array.from({ length: PIECE_COLS }, (_, i) => `岩心段${i + 1}`),
  'RQD(%)', '岩质', '岩体结构类型', '坚硬程度'];
headers.forEach((h, ci) => {
  const cell = ws.getCell(1, ci + 1);
  cell.value = h;
  cell.style = headerStyle;
});

const hints = new Array(headers.length).fill('');
hints[0] = HINT_MAIN_A;
hints[1] = '本回次钻进的米数，只填数字，如 1.5 表示1米5';
hints[2] = '由浅到深一段一格：整段只填长度（≥0.1m，如 1.2）；不足10厘米破碎带填 长度,块数（如 0.08,3）；空格跳过';
hints[COL_RQD - 1] = '自动计算，不要改';
hints[COL_ROCK - 1] = '选填，下拉选';
hints[COL_STRUCT - 1] = '选填，先选岩质';
hints[COL_HARD - 1] = '选填，下拉选';
hints.forEach((t, ci) => {
  const cell = ws.getCell(2, ci + 1);
  cell.value = t;
  cell.style = hintStyle;
});
ws.getRow(2).height = 42;

// 预格式化数据区：录入格黄底边框、进尺三位小数、RQD 公式
for (let r = FIRST_DATA_ROW; r <= LAST_ROW; r++) {
  for (let c = 1; c <= 2 + PIECE_COLS; c++) {
    const cell = ws.getCell(r, c);
    cell.style = inputStyle;
    if (c === 2) cell.numFmt = '0.000';
  }
  ws.getCell(r, COL_RQD).value = {
    formula: `IF(OR($B${r}="",$B${r}=0),"",ROUND(SUM($${FIRST_PIECE}${r}:$${LAST_PIECE}${r})/$B${r}*100,2))`,
  };
}

ws.getColumn(1).width = 8;
ws.getColumn(2).width = 12;
for (let c = 3; c <= 2 + PIECE_COLS; c++) ws.getColumn(c).width = 8;
ws.getColumn(COL_RQD).width = 9;
ws.getColumn(COL_ROCK).width = 10;
ws.getColumn(COL_STRUCT).width = 26;
ws.getColumn(COL_HARD).width = 10;

// 数据验证下拉（选填三列）。注意：exceljs 字段名是 formulae（不是 formulas，写错会静默丢下拉）
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
// 基本信息
// ---------------------------------------------------------------------------
const wsInfo = wb.addWorksheet('基本信息');
wsInfo.getColumn(1).width = 16;
wsInfo.getColumn(2).width = 30;
wsInfo.getCell(1, 1).value = '钻孔编号';
wsInfo.getCell(1, 1).style = headerStyle;
wsInfo.getCell(1, 2).style = inputStyle;
wsInfo.getCell(2, 1).value = '一个文件只记一个钻孔；编号建议与软件里的钻孔名一致，导入时自动核对。';
wsInfo.getCell(2, 1).font = { color: { argb: 'FF888888' }, size: 10 };

// ---------------------------------------------------------------------------
// 选项（隐藏表：结构类型 INDIRECT 联动的数据源）
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

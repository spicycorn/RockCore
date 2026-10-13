// 生成 RockCore 2.0 导入模板（v10 长表格式）→ app/src-tauri/resources/岩心回次统计导入模板.xlsx
//
// 设计约束（历史教训，勿改）：
//  · 无示例数据行——示例数据会被静默继承进真实项目（v4.1 教训）；
//  · 数值格一律纯数字、单位统一米，表头带单位；
//  · 「类型」列用数据验证下拉（完整|碎屑），杜绝手打错字；
//  · 版本拒收由导入引擎负责（表头 + 「数据录入」旧表探测），模板不放公式版本格。
import ExcelJS from 'exceljs';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const appDir = dirname(dirname(fileURLToPath(import.meta.url)));
const outPath = join(appDir, 'src-tauri', 'resources', '岩心回次统计导入模板.xlsx');
mkdirSync(dirname(outPath), { recursive: true });

const PREPARED_ROWS = 2000; // 预格式化行数（只设格式，不写值）

const wb = new ExcelJS.Workbook();
wb.creator = 'RockCore';
wb.lastModifiedBy = 'RockCore';

const headerStyle = {
  font: { bold: true, color: { argb: 'FFFFFFFF' } },
  fill: { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF2F5B8F' } },
  alignment: { horizontal: 'center', vertical: 'middle' },
  border: {
    top: { style: 'thin' }, left: { style: 'thin' },
    bottom: { style: 'thin' }, right: { style: 'thin' },
  },
};

// ---------------------------------------------------------------------------
// 填写说明
// ---------------------------------------------------------------------------
const guide = wb.addWorksheet('填写说明');
guide.getColumn(1).width = 100;
const lines = [
  ['RockCore 岩心回次统计导入模板（v10）'],
  [''],
  ['一、总体结构'],
  ['  · 「回次表」：一个回次一行，填 回次号 和 回次进尺(m)。行序 = 由浅到深的钻进顺序。'],
  ['  · 「岩心表」：一个岩心区间一行，填 回次号、类型、长度(m)、块数。同一回次的行按由浅到深排列。'],
  ['  · 所有长度单位一律为米（m），保留到毫米（3 位小数），单元格只填数字，不要带单位或文字。'],
  [''],
  ['二、岩心表怎么填'],
  ['  · 类型只有两种：「完整」= 单块长度 ≥10cm 的完整岩心；「碎屑」= <10cm 的破碎带（碎块/碎屑/岩粉）。'],
  ['  · 完整段：一行一块，只填长度，块数留空。'],
  ['  · 碎屑段：一行代表一段破碎带，长度 = 该破碎带总长，块数 = 其中碎块个数（不填按 1 块计）。'],
  ['  · 干净回次（全为完整段）也照常逐块填写；软件会自动把连续完整段并成一个记录段。'],
  [''],
  ['三、闭合校核（软件会逐回次自动检查）'],
  ['  · 同一回次「岩心表」所有行的长度合计，应等于「回次表」的进尺（允许 ±1cm）。'],
  ['  · 多记 → 导入被阻断；少记 → 警告，差额计入回次底部（磨损/漏记）。'],
  [''],
  ['四、示例（仅说明格式，请勿把示例抄进数据表）'],
  ['  回次表：回次 3，进尺 2.000'],
  ['  岩心表：3 | 完整 | 1.200 |（空）'],
  ['          3 | 完整 | 0.500 |（空）'],
  ['          3 | 碎屑 | 0.250 | 4'],
  ['  → 合计 1.950m，比进尺少 5cm，导入时提示少记警告。'],
  [''],
  ['五、不需要在本表填写的内容'],
  ['  · 岩质、岩体结构类型、坚硬程度、均一性、地下水、洞轴线夹角等围岩判级参数：'],
  ['    这些是室内/编录参数，导入后在软件「分段编辑」中录入或批量应用。'],
  ['  · RQD、平均间距、完整性等级等成果：全部由软件按原始数据重算，不要手填。'],
  [''],
  ['六、旧版模板（v9 及更早的 L1..L30 宽表）已停用，软件会整表拒收，请改用本模板。'],
];
lines.forEach((row, i) => {
  const cell = guide.getCell(i + 1, 1);
  cell.value = row[0];
  cell.alignment = { wrapText: true, vertical: 'top' };
  if (i === 0) cell.font = { bold: true, size: 14 };
  else if (row[0].startsWith('一、') || row[0].startsWith('二、') || row[0].startsWith('三、')
    || row[0].startsWith('四、') || row[0].startsWith('五、') || row[0].startsWith('六、')) {
    cell.font = { bold: true };
  }
});

// ---------------------------------------------------------------------------
// 回次表
// ---------------------------------------------------------------------------
const wsRun = wb.addWorksheet('回次表', { views: [{ state: 'frozen', ySplit: 1 }] });
wsRun.getColumn(1).width = 10;
wsRun.getColumn(2).width = 14;
wsRun.getCell(1, 1).value = '回次号';
wsRun.getCell(1, 2).value = '回次进尺(m)';
wsRun.getRow(1).eachCell((c) => { c.style = { ...c.style, ...headerStyle }; });
for (let r = 2; r <= PREPARED_ROWS + 1; r++) {
  wsRun.getCell(r, 1).numFmt = '0';
  wsRun.getCell(r, 2).numFmt = '0.000';
}

// ---------------------------------------------------------------------------
// 岩心表
// ---------------------------------------------------------------------------
const wsCore = wb.addWorksheet('岩心表', { views: [{ state: 'frozen', ySplit: 1 }] });
wsCore.getColumn(1).width = 10;
wsCore.getColumn(2).width = 10;
wsCore.getColumn(3).width = 12;
wsCore.getColumn(4).width = 10;
const coreHeaders = ['回次号', '类型', '长度(m)', '块数'];
coreHeaders.forEach((h, i) => {
  const c = wsCore.getCell(1, i + 1);
  c.value = h;
  c.style = { ...c.style, ...headerStyle };
});
for (let r = 2; r <= PREPARED_ROWS + 1; r++) {
  wsCore.getCell(r, 1).numFmt = '0';
  wsCore.getCell(r, 3).numFmt = '0.000';
  wsCore.getCell(r, 4).numFmt = '0';
}
wsCore.dataValidations.add(`B2:B${PREPARED_ROWS + 1}`, {
  type: 'list',
  allowBlank: true,
  formulae: ['"完整,碎屑"'],
  error: '类型只能选「完整」或「碎屑」',
  errorTitle: '输入无效',
  showErrorMessage: true,
});

await wb.xlsx.writeFile(outPath);
console.log(`模板已生成：${outPath}`);

/**
 * 表 F.0.4「岩体完整程度划分」判定引擎。
 *
 * 判定依据为可配置的 IntegrityLevelCriterion 表（规范设置中可编辑），
 * 配置为空时回退到内置默认表（DL/T 5894-2025 表 F.0.4 原样）。
 *
 * 自动判级（Excel 导入）走「估算组数 + 间距」联合查表：
 * 组数（结构面方位族数）无法从块数直接量测，但规范表的组数列与间距列单调相关
 * （>50cm↔1~2 组；10~50cm↔2~3 组；≤10cm↔2~3 组或 >3 组），唯一需要
 * 额外信息的是 ≤10cm 档——用碎屑（连续 <10cm 碎块，现场记「长度,块数」）收口：
 * 碎屑是多组结构面交叉切割的产物，其平均块径（碎屑长÷碎屑块数）低于阈值
 * （默认 3.3cm ≈ 10cm÷3 组）→ >3 组（很发育）；否则 → 2~3 组（发育）。
 * 估算规则见 estimateJointSetCount。
 * 破碎行（组数=无序、间距=—）没有间距判据，自动判级永不命中，仅人工覆盖。
 *
 * 区间记法（与规范表写法一致，大小写不敏感）：
 *   结构面发育组数（整数，tryMatch / 自动判级使用）：
 *     "1~2" → 1 ≤ J ≤ 2；">3" → J > 3；"—"/"不限"/空 → 不限制；
 *     "无序" → 无法解析为整数 → 该行永不自动命中（破碎行的规范语义）
 *   结构面间距（cm，实数）：
 *     "100~50" → 50 < S ≤ 100（下限开、上限闭，降序写法自动交换，与规范表一致）
 *     ">100" → S > 100；"≤10" → S ≤ 10；"—"/空 → 不限制（不参与间距匹配）
 *
 * 判定顺序：按表中行的先后顺序（第 1 行优先），首次匹配即返回。
 */

import { IntegrityLevel, parseIntegrityLevelKey as parseLevelKey } from './enums';
import { defaultIntegrityCriteria, type IntegrityLevelCriterion } from './config';

export interface SpacingRange {
  min: number;
  minInclusive: boolean;
  max: number;
  maxInclusive: boolean;
  /** true = 不限制（"—"/空） */
  any: boolean;
}

export interface CountRange {
  min: number;
  max: number;
  any: boolean;
}

const UNLIMITED_TOKENS = new Set(['—', '——', '-', '不限']);

/** 解析 C# 风格不变量浮点数（不接受 "1,5" 之类区域写法；也不接受 "0x10"/"1e5" 之外的怪写法——parseFloat 已足够严格对齐 double.TryParse(Float) 的常用输入面） */
function tryParseDouble(text: string): number | null {
  const s = text.trim();
  if (s.length === 0) return null;
  // double.TryParse(NumberStyles.Float, InvariantCulture) 接受：可选符号、小数、指数；不接受千分位/空白内嵌
  if (!/^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$/.test(s)) return null;
  const v = Number(s);
  return Number.isFinite(v) ? v : null;
}

function tryParseInt(text: string): number | null {
  const s = text.trim();
  if (!/^[+-]?\d+$/.test(s)) return null;
  return Number(s);
}

/**
 * 解析整数区间（结构面发育组数）。
 * 支持 "A~B"（闭区间）、">N"、"≥N"、"—"/空（不限制）、单值（精确匹配）。
 * 返回 null 表示无法解析（此时该行不参与匹配）。
 */
export function parseCountRange(text: string | undefined | null): CountRange | null {
  let s = (text ?? '').trim();
  if (s.length === 0 || UNLIMITED_TOKENS.has(s)) return { min: 0, max: Number.MAX_SAFE_INTEGER, any: true };

  s = s.replaceAll('＞', '>');

  if (s.startsWith('>')) {
    const n = tryParseInt(s.slice(1).trim());
    // J > n ⇔ J ≥ n+1（整数）
    return n == null ? null : { min: n + 1, max: Number.MAX_SAFE_INTEGER, any: false };
  }
  if (s.startsWith('≥')) {
    const n = tryParseInt(s.slice(1).trim());
    return n == null ? null : { min: n, max: Number.MAX_SAFE_INTEGER, any: false };
  }

  const tilde = indexOfTilde(s);
  if (tilde >= 0) {
    const a = tryParseInt(s.slice(0, tilde).trim());
    const b = tryParseInt(s.slice(tilde + 1).trim());
    if (a != null && b != null) {
      return { min: Math.min(a, b), max: Math.max(a, b), any: false };
    }
    return null;
  }

  const exact = tryParseInt(s);
  if (exact != null) return { min: exact, max: exact, any: false };
  return null;
}

/**
 * 解析实数区间（结构面间距 cm）。
 * 支持 "A~B"（A 开 B 闭）、">N"、">=N"、"<N"、"<=N"、"≤N"、"≥N"、"—"/空（不限制）、单值（精确匹配）。
 * 返回 null 表示无法解析（此时该行不参与匹配）。
 */
export function parseSpacingRange(text: string | undefined | null): SpacingRange | null {
  let s = (text ?? '').trim();
  if (s.length === 0 || UNLIMITED_TOKENS.has(s)) {
    return { min: Number.NEGATIVE_INFINITY, minInclusive: true, max: Number.POSITIVE_INFINITY, maxInclusive: true, any: true };
  }

  s = s.replaceAll('＞', '>').replaceAll('＜', '<').replaceAll('＝', '=');

  if (s.startsWith('>')) {
    const inclusive = s.length > 1 && s[1] === '=';
    const n = tryParseDouble(s.slice(inclusive ? 2 : 1).trim());
    return n == null ? null : { min: n, minInclusive: inclusive, max: Number.POSITIVE_INFINITY, maxInclusive: true, any: false };
  }
  if (s.startsWith('<')) {
    const inclusive = s.length > 1 && s[1] === '=';
    const n = tryParseDouble(s.slice(inclusive ? 2 : 1).trim());
    return n == null ? null : { min: Number.NEGATIVE_INFINITY, minInclusive: true, max: n, maxInclusive: inclusive, any: false };
  }
  if (s.startsWith('≤')) {
    const n = tryParseDouble(s.slice(1).trim());
    return n == null ? null : { min: Number.NEGATIVE_INFINITY, minInclusive: true, max: n, maxInclusive: true, any: false };
  }
  if (s.startsWith('≥')) {
    const n = tryParseDouble(s.slice(1).trim());
    return n == null ? null : { min: n, minInclusive: true, max: Number.POSITIVE_INFINITY, maxInclusive: true, any: false };
  }

  const tilde = indexOfTilde(s);
  if (tilde >= 0) {
    const a = tryParseDouble(s.slice(0, tilde).trim());
    const b = tryParseDouble(s.slice(tilde + 1).trim());
    if (a != null && b != null) {
      // A~B：下限开、上限闭（与规范表 ">50~100" 类写法一致）
      const [lo, hi] = a <= b ? [a, b] : [b, a];
      return { min: lo, minInclusive: false, max: hi, maxInclusive: true, any: false };
    }
    return null;
  }

  const exact = tryParseDouble(s);
  if (exact != null) return { min: exact, minInclusive: true, max: exact, maxInclusive: true, any: false };
  return null;
}

function indexOfTilde(s: string): number {
  for (let i = 0; i < s.length; i++) {
    if (s[i] === '~' || s[i] === '～') return i;
  }
  return -1;
}

/** 间距是否落在区间内（闭开语义由 range 携带） */
function spacingInRange(range: SpacingRange, spacingCm: number): boolean {
  if (range.minInclusive ? spacingCm < range.min : spacingCm <= range.min) return false;
  if (range.maxInclusive ? spacingCm > range.max : spacingCm >= range.max) return false;
  return true;
}

/**
 * 仅按「结构面间距」查找首次匹配行（按行序，第 1 行优先）——组数信息不可用时的容错路径。
 * 间距为「—/不限」（无间距判据）的行跳过：这类行（如破碎行）规范上就不该由间距自动命中。
 */
export function tryMatchBySpacing(
  criteria: IntegrityLevelCriterion[] | null | undefined,
  spacingCm: number,
): IntegrityLevelCriterion | null {
  if (criteria == null || criteria.length === 0) return null;
  for (const c of criteria) {
    const range = parseSpacingRange(c.jointSpacing);
    if (range == null || range.any) continue;
    if (spacingInRange(range, spacingCm)) return c;
  }
  return null;
}

/**
 * 在判定表中查找首次匹配行（组数 + 间距同时匹配；按行序，第 1 行优先）。
 * 保留给"组数确已知"的场景（人工地质描述）；Excel 导入自动判级走 tryMatchBySpacing。
 */
export function tryMatch(
  criteria: IntegrityLevelCriterion[] | null | undefined,
  jointCount: number,
  spacingCm: number,
): IntegrityLevelCriterion | null {
  if (criteria == null || criteria.length === 0) return null;
  for (const c of criteria) {
    const j = parseCountRange(c.jointSetCount);
    if (j == null) continue;
    if (jointCount < j.min || jointCount > j.max) continue;
    const s = parseSpacingRange(c.jointSpacing);
    if (s == null) continue;
    if (!spacingInRange(s, spacingCm)) continue;
    return c;
  }
  return null;
}

/**
 * 从回次取芯数据估算结构面发育组数（用于表 F.0.4 联合查表）。
 *
 * 规范表的组数列与间距列单调相关，估算只需还原这一对应：
 *   S > 50cm          → 2 组（表中 1~2 组档）
 *   10 < S ≤ 50cm     → 3 组（表中 2~3 组档）
 *   S ≤ 10cm          → 表中 2~3 组（完整性差·发育）与 >3 组（较破碎·很发育）并存。
 *                       组数是多组结构面交叉切割的产物，碎屑正是它的直接痕迹：
 *                       碎屑平均块径（碎屑长÷碎屑块数）< fineFragMeanCm
 *                       （默认 3.3cm ≈ 10cm÷3 组——3 组在 10cm 内均匀切割的块径）
 *                       → >3 组；块径不小于阈值或该段无碎屑 → 2~3 组。
 * 估算值只用于查表匹配；人工地质描述可在设置/分段页直接覆盖最终等级。
 */
export function estimateJointSetCount(
  spacingCm: number,
  fragMeanSizeCm: number,
  fineFragMeanCm = 3.3,
): number {
  if (spacingCm > 50) return 2;
  if (spacingCm > 10) return 3;
  return Number.isFinite(fragMeanSizeCm) && fragMeanSizeCm < fineFragMeanCm ? 4 : 3;
}

/**
 * 判级（导入自动判级入口）：估算组数 + 间距联合查表；组数条件不兼容（用户把组数
 * 改成与估算永远矛盾的值）时退回纯间距匹配；再无匹配按规范分档兜底，避免整孔 Unknown。
 * S≤0 说明该段没有任何结构面计数，保守按"完整性差"。
 * @param fragMeanSizeCm 段/行的碎屑平均块径（cm）；无碎屑传 NaN（≤10cm 档按 2~3 组）
 */
export function judgeIntegrity(
  spacingCm: number,
  criteria: IntegrityLevelCriterion[] | null | undefined,
  fragMeanSizeCm = Number.NaN,
  fineFragMeanCm = 3.3,
): IntegrityLevel {
  if (spacingCm <= 0) return IntegrityLevel.Poor;

  const list = criteria != null && criteria.length > 0 ? criteria : defaultIntegrityCriteria();
  const est = estimateJointSetCount(spacingCm, fragMeanSizeCm, fineFragMeanCm);
  const hit = tryMatch(list, est, spacingCm) ?? tryMatchBySpacing(list, spacingCm);
  if (hit != null) {
    const level = parseLevelKey(hit.levelKey);
    if (level !== IntegrityLevel.Unknown) return level;
  }

  // 用户把判定表改坏（区间写空/写错）导致无匹配时的兜底，分档与规范默认表一致。
  if (spacingCm > 100) return IntegrityLevel.Intact;
  if (spacingCm > 30) return IntegrityLevel.RelativelyIntact;
  if (spacingCm > 10) return IntegrityLevel.Poor;
  return estimateJointSetCount(spacingCm, fragMeanSizeCm, fineFragMeanCm) > 3
    ? IntegrityLevel.RelativelyBroken
    : IntegrityLevel.Poor;
}

export { parseIntegrityLevelKey as parseLevelKey } from './enums';

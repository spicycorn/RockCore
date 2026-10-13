/**
 * 表 F.0.4「岩体完整程度划分」判定引擎（逐行移植自 1.x IntegrityCriteriaEngine，语义保持一致）。
 *
 * 判定依据为可配置的 IntegrityLevelCriterion 表（规范设置中可编辑），
 * 配置为空时回退到内置默认表。
 *
 * 自动判级（Excel 导入）走 tryMatchBySpacing：**只按结构面间距查表**，
 * 「结构面发育组数」不参与匹配——组数是地质描述量，回次取芯数据算不出它。
 * tryMatch 保留给组数确已知的场景（人工地质描述）。
 *
 * 区间记法（与规范表写法一致，大小写不敏感）：
 *   结构面发育组数（整数，仅 tryMatch 使用）：
 *     "1~2" → 1 ≤ J ≤ 2；">3" → J > 3；"—"/"——"/空 → 不限制
 *   结构面间距（cm，实数）：
 *     "50~95" → 50 < S ≤ 95（下限开、上限闭，与规范表 ">50~100" 类写法一致）
 *     ">95" → S > 95；"<2" → S < 2；"≤10"/"<=10" → S ≤ 10；"—"/空 → 不限制
 *
 * 判定顺序：按表中行的先后顺序（第 1 行最严格），首次匹配即返回。
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
 * 仅按「结构面间距」查找首次匹配行（按行序，第 1 行优先）——自动判级的唯一入口。
 * 刻意忽略 jointSetCount：组数不是回次能测出的量，让它参与匹配只会让表形同虚设。
 * 无法解析间距的行跳过。
 */
export function tryMatchBySpacing(
  criteria: IntegrityLevelCriterion[] | null | undefined,
  spacingCm: number,
): IntegrityLevelCriterion | null {
  if (criteria == null || criteria.length === 0) return null;
  for (const c of criteria) {
    const range = parseSpacingRange(c.jointSpacing);
    if (range == null) continue;
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
 * 判级（导入自动判级入口）。只使用平均间距：「结构面发育组数」不参与匹配。
 * 默认表已覆盖 S>0 的全部区间；S≤0 说明该段没有任何结构面计数，保守按"完整性差"。
 */
export function judgeIntegrity(
  spacingCm: number,
  criteria: IntegrityLevelCriterion[] | null | undefined,
): IntegrityLevel {
  if (spacingCm <= 0) return IntegrityLevel.Poor;

  const list = criteria != null && criteria.length > 0 ? criteria : defaultIntegrityCriteria();
  const hit = tryMatchBySpacing(list, spacingCm);
  if (hit != null) {
    const level = parseLevelKey(hit.levelKey);
    if (level !== IntegrityLevel.Unknown) return level;
  }

  // 用户把判定表改坏（区间写空/写错）导致无匹配时按间距兜底，避免整孔 Unknown。
  // 分档与内置默认表一致。
  if (spacingCm > 95) return IntegrityLevel.Intact;
  if (spacingCm > 50) return IntegrityLevel.RelativelyIntact;
  if (spacingCm > 30) return IntegrityLevel.RelativelyIntact;
  if (spacingCm > 10) return IntegrityLevel.Poor;
  if (spacingCm > 2) return IntegrityLevel.RelativelyBroken;
  return IntegrityLevel.Broken;
}

export { parseIntegrityLevelKey as parseLevelKey } from './enums';

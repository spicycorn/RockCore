/**
 * 表 F.0.4 判定引擎黄金用例：区间解析 + 估算组数 + 联合查表判级。
 * 默认表逐行对应 DL/T 5894-2025 表 F.0.4（两个 <10 档写作 ≤10 闭合边界）。
 */
import { describe, expect, it } from 'vitest';
import {
  estimateJointSetCount, judgeIntegrity, parseCountRange, parseSpacingRange, tryMatch, tryMatchBySpacing,
} from '../integrity';
import { defaultIntegrityCriteria } from '../config';
import { IntegrityLevel, parseIntegrityLevelKey as parseLevelKey } from '../enums';

describe('parseSpacingRange', () => {
  it('A~B：下限开、上限闭', () => {
    const r = parseSpacingRange('50~95')!;
    expect(r.min).toBe(50);
    expect(r.minInclusive).toBe(false);
    expect(r.max).toBe(95);
    expect(r.maxInclusive).toBe(true);
  });

  it('全角波浪号等价', () => {
    expect(parseSpacingRange('50～95')).toEqual(parseSpacingRange('50~95'));
  });

  it('>/</≥/≤/<= 与全角符号', () => {
    expect(parseSpacingRange('>95')!.min).toBe(95);
    expect(parseSpacingRange('>95')!.minInclusive).toBe(false);
    expect(parseSpacingRange('>=10')!.minInclusive).toBe(true);
    expect(parseSpacingRange('＜2')!.max).toBe(2);
    expect(parseSpacingRange('≤10')!.maxInclusive).toBe(true);
  });

  it('—/——/不限 = 不限制', () => {
    for (const t of ['—', '——', '-', '不限', '']) {
      expect(parseSpacingRange(t)!.any).toBe(true);
    }
  });

  it('无法解析返回 null', () => {
    expect(parseSpacingRange('abc')).toBeNull();
    expect(parseSpacingRange('>abc')).toBeNull();
    expect(parseSpacingRange('10~abc')).toBeNull();
  });
});

describe('parseCountRange', () => {
  it('1~2 闭区间、>3 下限、单值精确', () => {
    expect(parseCountRange('1~2')).toEqual({ min: 1, max: 2, any: false });
    expect(parseCountRange('>3')).toEqual({ min: 4, max: Number.MAX_SAFE_INTEGER, any: false });
    expect(parseCountRange('2')).toEqual({ min: 2, max: 2, any: false });
    expect(parseCountRange('—')!.any).toBe(true);
  });

  it('「无序」（破碎行规范写法）无法解析为整数 → null，该行永不自动命中', () => {
    expect(parseCountRange('无序')).toBeNull();
  });
});

describe('estimateJointSetCount（组数估算）', () => {
  it('S>50 → 2 组；10<S≤50 → 3 组', () => {
    expect(estimateJointSetCount(1000, 1)).toBe(2);
    expect(estimateJointSetCount(50.01, 1)).toBe(2);
    expect(estimateJointSetCount(50, 0.5)).toBe(3);
    expect(estimateJointSetCount(10.01, 0.5)).toBe(3);
  });

  it('S≤10：碎屑平均块径 < 阈值 → 4 组（>3 档）；块径 ≥ 阈值或无碎屑 → 3 组', () => {
    expect(estimateJointSetCount(10, 3.29)).toBe(4);
    expect(estimateJointSetCount(10, 0.5)).toBe(4);
    expect(estimateJointSetCount(10, 3.3)).toBe(3);
    expect(estimateJointSetCount(5, 10)).toBe(3);
    expect(estimateJointSetCount(5, Number.NaN)).toBe(3);
  });
});

describe('judgeIntegrity（内置默认表 = 规范表 F.0.4）', () => {
  const criteria = defaultIntegrityCriteria();
  const level = (s: number, fragMean?: number) => judgeIntegrity(s, criteria, fragMean);

  it('S≤0 保守按完整性差', () => {
    expect(level(0)).toBe(IntegrityLevel.Poor);
    expect(level(-5)).toBe(IntegrityLevel.Poor);
  });

  it('间距分档与边界（下限开、上限闭；≤10 闭合 S=10）', () => {
    // >100 → 完整（1~2 组档）
    expect(level(100.01)).toBe(IntegrityLevel.Intact);
    expect(level(1000)).toBe(IntegrityLevel.Intact);
    // 50<S≤100 → 较完整（1~2 组档）
    expect(level(100)).toBe(IntegrityLevel.RelativelyIntact);
    expect(level(50.01)).toBe(IntegrityLevel.RelativelyIntact);
    // 30<S≤50 → 较完整（2~3 组档）
    expect(level(50)).toBe(IntegrityLevel.RelativelyIntact);
    expect(level(30.01)).toBe(IntegrityLevel.RelativelyIntact);
    // 10<S≤30 → 完整性差
    expect(level(30)).toBe(IntegrityLevel.Poor);
    expect(level(10.01)).toBe(IntegrityLevel.Poor);
  });

  it('S≤10 由碎屑平均块径收口：块径 <3.3cm → 较破碎；≥3.3cm 或无碎屑 → 完整性差', () => {
    expect(level(10, 2)).toBe(IntegrityLevel.RelativelyBroken);
    expect(level(1, 0.8)).toBe(IntegrityLevel.RelativelyBroken);
    expect(level(10, 3.3)).toBe(IntegrityLevel.Poor);
    expect(level(10, 8)).toBe(IntegrityLevel.Poor);
    // 无碎屑（NaN）→ 2~3 组 → 完整性差
    expect(level(5)).toBe(IntegrityLevel.Poor);
  });

  it('破碎永不自动命中（组数=无序、间距=— 均不参与）', () => {
    for (const s of [0.5, 1, 5, 10, 50, 100, 500]) {
      expect(level(s, 0)).not.toBe(IntegrityLevel.Broken);
    }
  });

  it('碎屑块径阈值可配（改 5 后块径 4cm 判较破碎）', () => {
    expect(judgeIntegrity(5, criteria, 4, 5)).toBe(IntegrityLevel.RelativelyBroken);
    expect(judgeIntegrity(5, criteria, 4, 3.3)).toBe(IntegrityLevel.Poor);
  });

  it('判定表被改坏（无匹配）时按间距兜底，分档与规范默认表一致', () => {
    const broken = [{ levelKey: 'Broken', jointSetCount: '—', jointSpacing: '乱码', jointDevelopment: '' }];
    expect(judgeIntegrity(101, broken)).toBe(IntegrityLevel.Intact);
    expect(judgeIntegrity(100, broken)).toBe(IntegrityLevel.RelativelyIntact);
    expect(judgeIntegrity(60, broken)).toBe(IntegrityLevel.RelativelyIntact);
    expect(judgeIntegrity(40, broken)).toBe(IntegrityLevel.RelativelyIntact);
    expect(judgeIntegrity(20, broken)).toBe(IntegrityLevel.Poor);
    expect(judgeIntegrity(5, broken)).toBe(IntegrityLevel.Poor);
    expect(judgeIntegrity(5, broken, 1)).toBe(IntegrityLevel.RelativelyBroken);
  });

  it('组数与估算永远矛盾时退回纯间距容错', () => {
    const c = defaultIntegrityCriteria();
    for (const row of c) row.jointSetCount = '9~9';
    expect(judgeIntegrity(100, c)).toBe(IntegrityLevel.RelativelyIntact);
    expect(judgeIntegrity(20, c)).toBe(IntegrityLevel.Poor);
  });

  it('间距为「—」的行不参与纯间距匹配（破碎行不被兜底命中）', () => {
    const c = [
      { levelKey: 'Broken', jointSetCount: '—', jointSpacing: '—', jointDevelopment: '' },
      { levelKey: 'Intact', jointSetCount: '—', jointSpacing: '>0', jointDevelopment: '' },
    ];
    expect(tryMatchBySpacing(c, 50)!.levelKey).toBe('Intact');
    expect(tryMatchBySpacing(c, 1)!.levelKey).toBe('Intact');
  });
});

describe('tryMatch（组数 + 间距，人工地质描述场景）', () => {
  it('组数不匹配则不命中', () => {
    const c = [{ levelKey: 'Poor', jointSetCount: '1~2', jointSpacing: '>0', jointDevelopment: '' }];
    expect(tryMatch(c, 1, 50)).not.toBeNull();
    expect(tryMatch(c, 3, 50)).toBeNull();
  });
});

describe('parseLevelKey', () => {
  it('键名 → 枚举；未知 → Unknown', () => {
    expect(parseLevelKey('Intact')).toBe(IntegrityLevel.Intact);
    expect(parseLevelKey('UserOverride')).toBe(IntegrityLevel.UserOverride);
    expect(parseLevelKey('乱码')).toBe(IntegrityLevel.Unknown);
    expect(parseLevelKey(null)).toBe(IntegrityLevel.Unknown);
  });
});

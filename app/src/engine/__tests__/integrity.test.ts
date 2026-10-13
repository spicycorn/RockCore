/**
 * 表 F.0.4 判定引擎黄金用例：区间解析 + 按间距判级。
 * 用例语义与 1.x IntegrityCriteriaEngine + ExcelImportService.Judge 的行为一一对应。
 */
import { describe, expect, it } from 'vitest';
import { judgeIntegrity, parseCountRange, parseSpacingRange, tryMatch, tryMatchBySpacing } from '../integrity';
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
});

describe('judgeIntegrity（内置默认表）', () => {
  const criteria = defaultIntegrityCriteria();
  const level = (s: number) => judgeIntegrity(s, criteria);

  it('S≤0 保守按完整性差', () => {
    expect(level(0)).toBe(IntegrityLevel.Poor);
    expect(level(-5)).toBe(IntegrityLevel.Poor);
  });

  it('完整分档与边界（下限开、上限闭）', () => {
    expect(level(1)).toBe(IntegrityLevel.Broken);
    expect(level(1.99)).toBe(IntegrityLevel.Broken);
    // S=2 恰落在 "<2"（不含）与 "2~10"（下限开）之间的缝隙 → 兜底 Broken（与 1.x Judge 一致）
    expect(level(2)).toBe(IntegrityLevel.Broken);
    expect(level(2.01)).toBe(IntegrityLevel.RelativelyBroken);
    expect(level(10)).toBe(IntegrityLevel.RelativelyBroken);
    expect(level(10.01)).toBe(IntegrityLevel.Poor);
    expect(level(30)).toBe(IntegrityLevel.Poor);
    expect(level(30.01)).toBe(IntegrityLevel.RelativelyIntact);
    expect(level(50)).toBe(IntegrityLevel.RelativelyIntact);
    expect(level(95)).toBe(IntegrityLevel.RelativelyIntact);
    expect(level(95.01)).toBe(IntegrityLevel.Intact);
    expect(level(1000)).toBe(IntegrityLevel.Intact);
  });

  it('判定表被改坏（无匹配）时按间距兜底，分档与默认表一致', () => {
    const broken = [{ levelKey: 'Broken', jointSetCount: '—', jointSpacing: '乱码', jointDevelopment: '' }];
    expect(judgeIntegrity(100, broken)).toBe(IntegrityLevel.Intact);
    expect(judgeIntegrity(60, broken)).toBe(IntegrityLevel.RelativelyIntact);
    expect(judgeIntegrity(40, broken)).toBe(IntegrityLevel.RelativelyIntact);
    expect(judgeIntegrity(20, broken)).toBe(IntegrityLevel.Poor);
    expect(judgeIntegrity(5, broken)).toBe(IntegrityLevel.RelativelyBroken);
    expect(judgeIntegrity(1, broken)).toBe(IntegrityLevel.Broken);
  });

  it('忽略组数：组数写死也不影响自动判级', () => {
    const c = defaultIntegrityCriteria();
    c[0].jointSetCount = '9~9';
    expect(judgeIntegrity(1, c)).toBe(IntegrityLevel.Broken);
  });

  it('无法解析的行跳过', () => {
    const c = [
      { levelKey: 'Broken', jointSetCount: '—', jointSpacing: '乱码', jointDevelopment: '' },
      { levelKey: 'Intact', jointSetCount: '—', jointSpacing: '>0', jointDevelopment: '' },
    ];
    expect(tryMatchBySpacing(c, 50)!.levelKey).toBe('Intact');
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

/**
 * 规范配置（用户可编辑的判定依据）。
 *
 * 对应 1.x 的 RockSpecificationConfig + SpecificationService：
 * 词汇映射、数值阈值、岩体结构映射（表 F.0.2 的级联约束）、完整程度划分表（表 F.0.4）。
 * 2.0 中整份配置以 JSON 形式存于应用数据库 settings 表（键 specification），
 * 缺失字段按默认值补齐；用户已修改的内容绝不覆盖。
 */

import {
  IntegrityLevel,
  RockStructureType,
  RockType,
  integrityLevelName,
  rockTypeName,
} from './enums';

/** 表 F.0.4 的一行：单个完整性等级的判定指标 */
export interface IntegrityLevelCriterion {
  /** 完整性等级键名，如 Intact / RelativelyIntact / Poor / RelativelyBroken / Broken */
  levelKey: string;
  /**
   * 结构面发育组数，如 "1~2"、">3"、"无序"。
   * 自动判级使用「估算组数」参与匹配（见 integrity.estimateJointSets：
   * 间距 >50cm→2 组；10~50cm→3 组；≤10cm 按碎屑平均块径分 3 或 4(>3) 组；"无序" 永不自动命中）。
   * 人工地质描述场景可直接给出真实组数走 tryMatch。
   */
  jointSetCount: string;
  /** 结构面间距（cm），如 ">100"。自动判级的间距条件。"—"（无判据）的行不参与自动判级。 */
  jointSpacing: string;
  /** 结构面发育程度，如 "不发育"。地质描述字段，不参与判级。 */
  jointDevelopment: string;
}

export interface SpecThresholds {
  /** 洞轴线与岩层走向夹角判定阈值（度，默认 30） */
  caveAxisAngleThreshold: number;
  /**
   * 是否把相邻回次归并成「岩体段」后再判完整程度（默认 true）。
   * 规范的评价单元是"岩体性质相对均一的连续段"，回次只是采样单元；
   * 置为 false 时退回"一个记录段 = 一个完整性分段"的旧行为。
   */
  mergeRunsIntoRockSegments: boolean;
  /**
   * 岩体段最小厚度（m，默认 1.0）。归并后仍不足该厚度的薄段，仅当与相邻段间距相容时并入；
   * 间距不相容的薄碎屑带保留为独立段。
   */
  rockSegmentMinThicknessM: number;
  /** 归并容差：平均间距相对差超过该比例时另起一段（默认 0.5，即 50%）。 */
  rockSegmentSpacingToleranceRatio: number;
  /**
   * 组数估算阈值（cm，默认 3.3）：平均间距 ≤10cm 时，碎屑平均块径（碎屑长÷碎屑块数）
   * 低于该值判为多组交叉切割（>3 组 → 较破碎），否则按 2~3 组（完整性差）。
   * 3.3 ≈ 10cm÷3 组——3 组结构面在 10cm 内均匀切割的块径。对应规范表 F.0.4 中
   * "<10cm" 两列（完整性差·发育 / 较破碎·很发育）在回次数据下的区分信号。
   */
  fragMeanSizeThresholdCm: number;
}

export interface RockSpecificationConfig {
  /** 词汇映射：枚举名 → 显示文本（硬质岩/完整/…） */
  terminology: Record<string, string>;
  thresholds: SpecThresholds;
  /**
   * 岩体结构映射：`"${RockType名}_${IntegrityLevel名}"` → 可选结构类型编码列表。
   * 严格对应规范表 F.0.2 的级联约束。
   */
  structureMappings: Record<string, number[]>;
  /** 表 F.0.4 完整程度划分表；行序 = 优先级（首次匹配即返回） */
  integrityLevelCriteria: IntegrityLevelCriterion[];
}

/**
 * 内置默认判定表（表 F.0.4，DL/T 5894-2025 附录 F）。行序 = 优先级，首次匹配即返回。
 *
 * 与规范表逐列对应：组数 / 间距 / 发育程度。两处工程化收口（均已在设置页可见）：
 *  · 规范两个 "<10" 列写作 "≤10"，闭合 S=10 的边界缝隙；
 *  · 破碎行组数 = "无序"、间距 = "—"：规范未给间距判据（无序结构是地质描述量），
 *    自动判级永不命中，只能由人工在分段页覆盖。
 */
export function defaultIntegrityCriteria(): IntegrityLevelCriterion[] {
  return [
    { levelKey: 'Broken', jointSetCount: '无序', jointSpacing: '—', jointDevelopment: '—' },
    { levelKey: 'RelativelyBroken', jointSetCount: '>3', jointSpacing: '≤10', jointDevelopment: '很发育' },
    { levelKey: 'Poor', jointSetCount: '2~3', jointSpacing: '≤10', jointDevelopment: '发育' },
    { levelKey: 'Poor', jointSetCount: '2~3', jointSpacing: '30~10', jointDevelopment: '较发育' },
    { levelKey: 'RelativelyIntact', jointSetCount: '2~3', jointSpacing: '50~30', jointDevelopment: '中等发育' },
    { levelKey: 'RelativelyIntact', jointSetCount: '1~2', jointSpacing: '100~50', jointDevelopment: '轻度发育' },
    { levelKey: 'Intact', jointSetCount: '1~2', jointSpacing: '>100', jointDevelopment: '不发育' },
  ];
}

/**
 * 旧版（≤2.0.3）内置默认表。仅用于数据库迁移识别：库里存的判定表若与它逐字段相同，
 * 说明用户从未改过判定表、只是被旧默认值污染，加载时自动替换为现行规范默认表。
 */
export function legacyIntegrityCriteriaV1(): IntegrityLevelCriterion[] {
  return [
    { levelKey: 'Broken', jointSetCount: '—', jointSpacing: '<2', jointDevelopment: '' },
    { levelKey: 'RelativelyBroken', jointSetCount: '—', jointSpacing: '2~10', jointDevelopment: '' },
    { levelKey: 'Poor', jointSetCount: '—', jointSpacing: '10~30', jointDevelopment: '' },
    { levelKey: 'RelativelyIntact', jointSetCount: '—', jointSpacing: '30~50', jointDevelopment: '' },
    { levelKey: 'RelativelyIntact', jointSetCount: '—', jointSpacing: '50~95', jointDevelopment: '' },
    { levelKey: 'Intact', jointSetCount: '—', jointSpacing: '>95', jointDevelopment: '' },
  ];
}

export function defaultTerminology(): Record<string, string> {
  return {
    HardRock: '硬质岩',
    SoftRock: '软质岩',
    Intact: '完整',
    RelativelyIntact: '较完整',
    Poor: '完整性差',
    RelativelyBroken: '较破碎',
    Broken: '破碎',
  };
}

export function defaultThresholds(): SpecThresholds {
  return {
    caveAxisAngleThreshold: 30.0,
    mergeRunsIntoRockSegments: true,
    rockSegmentMinThicknessM: 1.0,
    rockSegmentSpacingToleranceRatio: 0.5,
    fragMeanSizeThresholdCm: 3.3,
  };
}

/** 结构映射键：与 1.x 的 `$"{RockType}_{IntegrityLevel}"` 完全一致 */
export function structureKey(rockType: RockType, level: IntegrityLevel): string {
  return `${rockTypeName[rockType]}_${integrityLevelName[level]}`;
}

const S = RockStructureType;

/** 岩体结构映射默认值（严格对应规范表 F.0.2，与 1.x SpecificationService 一致） */
export function defaultStructureMappings(): Record<string, number[]> {
  const m: Record<string, number[]> = {};
  const add = (rt: RockType, lvl: IntegrityLevel, structures: RockStructureType[]) => {
    m[structureKey(rt, lvl)] = structures.map(Number);
  };
  // 硬质岩
  add(RockType.HardRock, IntegrityLevel.Intact, [S.Massive]);
  add(RockType.HardRock, IntegrityLevel.RelativelyIntact, [S.Blocky, S.SubBlocky, S.ThickLayered, S.Interbedded]);
  add(RockType.HardRock, IntegrityLevel.Poor, [S.ThinLayered, S.Mosaic, S.BlockyFractured]);
  add(RockType.HardRock, IntegrityLevel.RelativelyBroken, [S.Cataclastic]);
  add(RockType.HardRock, IntegrityLevel.Broken, [S.GranularHard]);
  // 软质岩
  add(RockType.SoftRock, IntegrityLevel.Intact, [S.Massive]);
  add(RockType.SoftRock, IntegrityLevel.RelativelyIntact, [S.BlockyOrSubBlocky, S.ThickOrInterbedded]);
  add(RockType.SoftRock, IntegrityLevel.Poor, [S.ThinOrBlockyFractured]);
  add(RockType.SoftRock, IntegrityLevel.RelativelyBroken, [S.Cataclastic]);
  add(RockType.SoftRock, IntegrityLevel.Broken, [S.GranularSoft]);
  return m;
}

export function defaultConfig(): RockSpecificationConfig {
  return {
    terminology: defaultTerminology(),
    thresholds: defaultThresholds(),
    structureMappings: defaultStructureMappings(),
    integrityLevelCriteria: defaultIntegrityCriteria(),
  };
}

/**
 * 补齐缺失字段（浅合并语义）：
 * 仅当某项缺失或为空时填默认值；用户已修改的判定表/映射/词汇绝不覆盖。
 */
export function ensureDefaults(config: Partial<RockSpecificationConfig> | null | undefined): RockSpecificationConfig {
  const out = defaultConfig();
  if (config?.terminology && Object.keys(config.terminology).length > 0) out.terminology = config.terminology;
  if (config?.thresholds) out.thresholds = { ...out.thresholds, ...config.thresholds };
  if (config?.structureMappings && Object.keys(config.structureMappings).length > 0) {
    out.structureMappings = config.structureMappings;
  }
  if (config?.integrityLevelCriteria && config.integrityLevelCriteria.length > 0) {
    out.integrityLevelCriteria = config.integrityLevelCriteria;
  }
  return out;
}

/**
 * 岩质 + 完整性等级 → 可选岩体结构类型（表 F.0.2 级联约束）。
 * 岩质未设置时返回全部结构类型；映射缺失时回退到内置默认映射，
 * 仍缺失时按岩质返回全量列表（与 1.x SpecificationService.GetAvailableStructures 一致）。
 */
export function availableStructures(
  config: RockSpecificationConfig,
  rockType: RockType,
  level: IntegrityLevel,
): RockStructureType[] {
  if (rockType === RockType.NotSet) {
    return allStructureTypes();
  }
  const key = structureKey(rockType, level);
  const list = config.structureMappings[key] ?? defaultStructureMappings()[key];
  if (list != null) return [...list] as RockStructureType[];
  return rockType === RockType.HardRock
    ? [S.Massive, S.Blocky, S.SubBlocky, S.ThickLayered, S.Interbedded, S.ThinLayered, S.Mosaic, S.BlockyFractured, S.Cataclastic, S.GranularHard]
    : [S.Massive, S.BlockyOrSubBlocky, S.ThickOrInterbedded, S.ThinOrBlockyFractured, S.Cataclastic, S.GranularSoft];
}

function allStructureTypes(): RockStructureType[] {
  return [
    S.Massive, S.Cataclastic, S.Blocky, S.SubBlocky, S.ThickLayered, S.Interbedded,
    S.ThinLayered, S.Mosaic, S.BlockyFractured, S.GranularHard,
    S.BlockyOrSubBlocky, S.ThickOrInterbedded, S.ThinOrBlockyFractured, S.GranularSoft,
  ];
}

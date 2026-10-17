/**
 * 领域枚举 —— 与 1.x（C#）逐值对齐，数值编码保持完全一致，
 * 因为数据库以整数存储这些枚举。中文标签取自原 [Description] 注解。
 */

/** 围岩类别（DL/T 5894-2025 表 F.0.2） */
export enum RockClass {
  I = 0,
  II = 1,
  III = 2,
  IV = 3,
  V = 4,
}

/** 完整性等级（表 F.0.4 判级结果） */
export enum IntegrityLevel {
  Unknown = 0,
  Intact = 1,
  RelativelyIntact = 2,
  Poor = 3,
  RelativelyBroken = 4,
  Broken = 5,
  /** 用户手工指定 */
  UserOverride = 99,
}

/** 岩质类型 */
export enum RockType {
  NotSet = 0,
  HardRock = 1,
  SoftRock = 2,
}

/**
 * 岩体结构类型（表 F.0.2 的岩体结构列）。
 * 硬质岩 10 种、软质岩 6 种，其中「整体状或巨厚层状结构」「碎裂结构」两种共享。
 */
export enum RockStructureType {
  NotSet = 0,
  // 共享
  Massive = 1,
  Cataclastic = 2,
  // 硬质岩独有
  Blocky = 3,
  SubBlocky = 4,
  ThickLayered = 5,
  Interbedded = 6,
  ThinLayered = 7,
  Mosaic = 8,
  BlockyFractured = 9,
  GranularHard = 10,
  // 软质岩独有
  BlockyOrSubBlocky = 11,
  ThickOrInterbedded = 12,
  ThinOrBlockyFractured = 13,
  GranularSoft = 14,
}

/** 岩石坚硬程度细分 */
export enum RockHardnessLevel {
  NotSet = 0,
  StrongRock = 1,
  MediumHardRock = 2,
  RelativelySoftRock = 3,
  SoftRock = 4,
}

/** 岩质均一性及软弱夹层 */
export enum RockHomogeneity {
  NotSet = 0,
  HomogeneousNoWeakLayer = 1,
  HasWeakLayer = 2,
}

/** 地下水条件（v1.1.2 起规范口径只分有/无；0=自动按水位判定；2 为旧档已并入 3） */
export enum GroundwaterCondition {
  NotSet = 0,
  Dry = 1,
  Damp = 2,
  Wet = 3,
}

// ---------------------------------------------------------------------------
// 中文标签（单一来源，UI / 导出 / 提示统一从这里取）
// ---------------------------------------------------------------------------

export const integrityLevelLabel: Record<IntegrityLevel, string> = {
  [IntegrityLevel.Unknown]: '未评定',
  [IntegrityLevel.Intact]: '完整',
  [IntegrityLevel.RelativelyIntact]: '较完整',
  [IntegrityLevel.Poor]: '完整性差',
  [IntegrityLevel.RelativelyBroken]: '较破碎',
  [IntegrityLevel.Broken]: '破碎',
  [IntegrityLevel.UserOverride]: '用户指定',
};

export const rockTypeLabel: Record<RockType, string> = {
  [RockType.NotSet]: '未设置',
  [RockType.HardRock]: '硬质岩',
  [RockType.SoftRock]: '软质岩',
};

export const rockStructureLabel: Record<RockStructureType, string> = {
  [RockStructureType.NotSet]: '未设置',
  [RockStructureType.Massive]: '整体状或巨厚层状结构',
  [RockStructureType.Cataclastic]: '碎裂结构',
  [RockStructureType.Blocky]: '块状结构',
  [RockStructureType.SubBlocky]: '次块状结构',
  [RockStructureType.ThickLayered]: '厚层状或中厚层状结构',
  [RockStructureType.Interbedded]: '互层状结构',
  [RockStructureType.ThinLayered]: '薄层状结构',
  [RockStructureType.Mosaic]: '镶嵌结构',
  [RockStructureType.BlockyFractured]: '块裂结构',
  [RockStructureType.GranularHard]: '碎块状或碎屑状结构',
  [RockStructureType.BlockyOrSubBlocky]: '块状或次块状结构',
  [RockStructureType.ThickOrInterbedded]: '厚层、中厚层或互层状结构',
  [RockStructureType.ThinOrBlockyFractured]: '薄层状或块裂结构',
  [RockStructureType.GranularSoft]: '碎块状或碎屑状散体结构',
};

export const rockHardnessLabel: Record<RockHardnessLevel, string> = {
  [RockHardnessLevel.NotSet]: '未设置',
  [RockHardnessLevel.StrongRock]: '坚硬岩',
  [RockHardnessLevel.MediumHardRock]: '中硬岩',
  [RockHardnessLevel.RelativelySoftRock]: '较软岩',
  [RockHardnessLevel.SoftRock]: '软岩',
};

export const rockHomogeneityLabel: Record<RockHomogeneity, string> = {
  [RockHomogeneity.NotSet]: '未设置',
  [RockHomogeneity.HomogeneousNoWeakLayer]: '均一无软弱夹层',
  [RockHomogeneity.HasWeakLayer]: '有软弱夹层',
};

export const groundwaterLabel: Record<GroundwaterCondition, string> = {
  [GroundwaterCondition.NotSet]: '自动（按水位）',
  [GroundwaterCondition.Dry]: '无水',
  [GroundwaterCondition.Damp]: '有水（渗水）',
  [GroundwaterCondition.Wet]: '有水',
};

/** 围岩类别罗马数字（Unicode 罗马数字，符合规范表书写习惯） */
export function rockClassNumeral(rockClass: RockClass): string {
  switch (rockClass) {
    case RockClass.I: return 'Ⅰ';
    case RockClass.II: return 'Ⅱ';
    case RockClass.III: return 'Ⅲ';
    case RockClass.IV: return 'Ⅳ';
    case RockClass.V: return 'Ⅴ';
  }
}

/** 带「类」后缀，如「Ⅲ类」 */
export function rockClassText(rockClass: RockClass): string {
  return `${rockClassNumeral(rockClass)}类`;
}

// ---------------------------------------------------------------------------
// 枚举名（配置键用，如 structureMappings 的 "HardRock_Intact"）
// ---------------------------------------------------------------------------

export const rockTypeName: Record<RockType, string> = {
  [RockType.NotSet]: 'NotSet',
  [RockType.HardRock]: 'HardRock',
  [RockType.SoftRock]: 'SoftRock',
};

export const integrityLevelName: Record<IntegrityLevel, string> = {
  [IntegrityLevel.Unknown]: 'Unknown',
  [IntegrityLevel.Intact]: 'Intact',
  [IntegrityLevel.RelativelyIntact]: 'RelativelyIntact',
  [IntegrityLevel.Poor]: 'Poor',
  [IntegrityLevel.RelativelyBroken]: 'RelativelyBroken',
  [IntegrityLevel.Broken]: 'Broken',
  [IntegrityLevel.UserOverride]: 'UserOverride',
};

/** 解析完整性等级键名（配置表 LevelKey 列）；无法识别返回 Unknown */
export function parseIntegrityLevelKey(levelKey: string | undefined | null): IntegrityLevel {
  switch ((levelKey ?? '').trim()) {
    case 'Intact': return IntegrityLevel.Intact;
    case 'RelativelyIntact': return IntegrityLevel.RelativelyIntact;
    case 'Poor': return IntegrityLevel.Poor;
    case 'RelativelyBroken': return IntegrityLevel.RelativelyBroken;
    case 'Broken': return IntegrityLevel.Broken;
    case 'UserOverride': return IntegrityLevel.UserOverride;
    default: return IntegrityLevel.Unknown;
  }
}

// ---------------------------------------------------------------------------
// 中文标签 → 枚举（Excel 导入用；模板下拉选项与这里必须逐字一致）
// ---------------------------------------------------------------------------

/** 空文本 → NotSet；无法识别 → null（调用方决定报错方式） */
function parseByLabel<T extends number>(text: string, map: Record<string, T>, notSet: T): T | null {
  const s = text.trim();
  if (s === '') return notSet;
  return map[s] ?? null;
}

const rockTypeByLabel: Record<string, RockType> = { 硬质岩: RockType.HardRock, 软质岩: RockType.SoftRock };
const structureByLabel: Record<string, RockStructureType> = {
  整体状或巨厚层状结构: RockStructureType.Massive,
  碎裂结构: RockStructureType.Cataclastic,
  块状结构: RockStructureType.Blocky,
  次块状结构: RockStructureType.SubBlocky,
  厚层状或中厚层状结构: RockStructureType.ThickLayered,
  互层状结构: RockStructureType.Interbedded,
  薄层状结构: RockStructureType.ThinLayered,
  镶嵌结构: RockStructureType.Mosaic,
  块裂结构: RockStructureType.BlockyFractured,
  '碎块状或碎屑状结构': RockStructureType.GranularHard,
  块状或次块状结构: RockStructureType.BlockyOrSubBlocky,
  '厚层、中厚层或互层状结构': RockStructureType.ThickOrInterbedded,
  薄层状或块裂结构: RockStructureType.ThinOrBlockyFractured,
  碎块状或碎屑状散体结构: RockStructureType.GranularSoft,
};
const hardnessByLabel: Record<string, RockHardnessLevel> = {
  坚硬岩: RockHardnessLevel.StrongRock,
  中硬岩: RockHardnessLevel.MediumHardRock,
  较软岩: RockHardnessLevel.RelativelySoftRock,
  软岩: RockHardnessLevel.SoftRock,
};

export function parseRockTypeLabel(text: string): RockType | null {
  return parseByLabel(text, rockTypeByLabel, RockType.NotSet);
}
export function parseStructureLabel(text: string): RockStructureType | null {
  return parseByLabel(text, structureByLabel, RockStructureType.NotSet);
}
export function parseHardnessLabel(text: string): RockHardnessLevel | null {
  return parseByLabel(text, hardnessByLabel, RockHardnessLevel.NotSet);
}

/** 硬质岩专属结构（表 F.0.2 硬质岩半区）；共享项=整体状或巨厚层状结构、碎裂结构 不在其中 */
export const hardRockOnlyStructures: ReadonlySet<RockStructureType> = new Set([
  RockStructureType.Blocky, RockStructureType.SubBlocky, RockStructureType.ThickLayered,
  RockStructureType.Interbedded, RockStructureType.ThinLayered, RockStructureType.Mosaic,
  RockStructureType.BlockyFractured, RockStructureType.GranularHard,
]);

/** 软质岩专属结构（表 F.0.2 软质岩半区） */
export const softRockOnlyStructures: ReadonlySet<RockStructureType> = new Set([
  RockStructureType.BlockyOrSubBlocky, RockStructureType.ThickOrInterbedded,
  RockStructureType.ThinOrBlockyFractured, RockStructureType.GranularSoft,
]);

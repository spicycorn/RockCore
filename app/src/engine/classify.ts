/**
 * 围岩分类管线（逐行移植自 1.x ClassificationService）。
 * 对岩体段逐段执行 F.0.2 查表；信息不足的段返回未分类并给出可操作的补充提示。
 */

import { classify, type ClassifyInput } from './classificationTable';
import {
  GroundwaterCondition,
  IntegrityLevel,
  RockClass,
  RockHardnessLevel,
  RockHomogeneity,
  RockStructureType,
  RockType,
  rockClassText,
} from './enums';


/** 一个岩体段的分类结果 */
export interface SegmentClassification {
  /** 段的标识（RockMassRunInput.key 的并集，用于 UI 定位；取组内第一个回次 key） */
  segmentKey: number;
  depthStart: number;
  depthEnd: number;
  /** 完整性等级（自动判级或用户覆盖后的最终值） */
  integrityLevel: IntegrityLevel;
  /** 分类结果（success=false 时 rockClass 无意义，看 missingFields） */
  result: ReturnType<typeof classify>;
  /** 判定置信度：成功=1.0；未分类=0 */
  confidenceScore: number;
}

/** 整孔分类汇总（诊断信息用于 UI 提示"还差哪些数据"） */
export interface ClassificationSummary {
  classifications: SegmentClassification[];
  /** 参与分类的段数（排除等级未设置的段） */
  totalInfoSegments: number;
  /** 已填岩质的段数 */
  segmentsWithRockType: number;
  /** 已填结构类型的段数 */
  segmentsWithStructure: number;
  /** 各完整性等级下缺岩质的段数（用于"哪些段还没法分类"提示） */
  missingRockTypeByLevel: Partial<Record<IntegrityLevel, number>>;
}

/** 段的 F.0.2 参数来源（由导入/编辑层提供，本模块不关心来源） */
export interface SegmentF02 {
  rockType: RockType;
  structureType: RockStructureType;
  hardnessLevel: RockHardnessLevel;
  homogeneity: RockHomogeneity;
  groundwater: GroundwaterCondition;
  caveAxisAngleLessThan30: boolean | null;
}

/** 段的最小结构：分类只需要深度范围与回次键 */
export interface ClassifiableSegment {
  depthStart: number;
  depthEnd: number;
  runs: readonly { key: number }[];
}

/**
 * 对段集合逐段分类。
 * @param segments 岩体段（自动按 depthStart 排序）
 * @param integrityLevels 每段的最终完整性等级（key → IntegrityLevel；含用户覆盖）
 * @param f02Of 每段的 F.0.2 参数（key → SegmentF02）
 */
export function classifySegments(
  segments: readonly ClassifiableSegment[],
  integrityLevels: ReadonlyMap<number, IntegrityLevel>,
  f02Of: (segment: ClassifiableSegment) => SegmentF02,
): ClassificationSummary {
  const classifications: SegmentClassification[] = [];
  let totalInfoSegments = 0;
  let withRockType = 0;
  let withStructure = 0;
  const missingRockTypeByLevel: Partial<Record<IntegrityLevel, number>> = {};

  const sorted = [...segments].sort((a, b) => a.depthStart - b.depthStart);
  for (const seg of sorted) {
    const key = seg.runs[0]?.key ?? 0;
    const level = integrityLevels.get(key) ?? IntegrityLevel.Unknown;
    const f02 = f02Of(seg);

    const base = {
      segmentKey: key,
      depthStart: seg.depthStart,
      depthEnd: seg.depthEnd,
      integrityLevel: level,
    };

    // 等级未设置/用户标记为未判级 → 未分类（对齐 1.x：UserOverride 与 Unknown 同样跳过查表）
    if (level === IntegrityLevel.Unknown || level === IntegrityLevel.UserOverride) {
      classifications.push({
        ...base,
        result: { success: false, rockClass: RockClass.I, basis: '完整性等级未设置', missingFields: ['完整性等级'] },
        confidenceScore: 0,
      });
      continue;
    }

    totalInfoSegments++;
    if (f02.rockType !== RockType.NotSet) withRockType++;
    if (f02.structureType !== RockStructureType.NotSet) withStructure++;

    if (f02.rockType === RockType.NotSet) {
      missingRockTypeByLevel[level] = (missingRockTypeByLevel[level] ?? 0) + 1;
      classifications.push({
        ...base,
        result: { success: false, rockClass: RockClass.I, basis: '岩质类型未设置，请选择硬质岩或软质岩', missingFields: ['岩质类型'] },
        confidenceScore: 0,
      });
      continue;
    }
    if (f02.structureType === RockStructureType.NotSet) {
      classifications.push({
        ...base,
        result: { success: false, rockClass: RockClass.I, basis: '岩体结构类型未设置', missingFields: ['岩体结构类型'] },
        confidenceScore: 0,
      });
      continue;
    }

    const input: ClassifyInput = {
      rockType: f02.rockType,
      structureType: f02.structureType,
      integrityLevel: level,
      groundwater: f02.groundwater,
      hardnessLevel: f02.hardnessLevel,
      homogeneity: f02.homogeneity,
      caveAxisAngleLessThan30: f02.caveAxisAngleLessThan30,
    };
    const result = classify(input);
    classifications.push({ ...base, result, confidenceScore: result.success ? 1.0 : 0 });
  }

  return {
    classifications,
    totalInfoSegments,
    segmentsWithRockType: withRockType,
    segmentsWithStructure: withStructure,
    missingRockTypeByLevel,
  };
}

/** 未分类结果的展示文本（对齐 1.x "未分类（原因）" 格式） */
export function classificationDisplayText(c: SegmentClassification): string {
  return c.result.success ? rockClassText(c.result.rockClass) : `未分类（${c.result.basis}）`;
}

/** 汇总缺项提示（用于导入完成后的引导文案） */
export function missingFieldsHint(summary: ClassificationSummary): string | null {
  const parts: string[] = [];
  const missingRock = summary.totalInfoSegments - summary.segmentsWithRockType;
  const missingStruct = summary.totalInfoSegments - summary.segmentsWithStructure;
  if (missingRock > 0) parts.push(`${missingRock} 段缺岩质类型`);
  if (missingStruct > 0) parts.push(`${missingStruct} 段缺岩体结构类型`);
  const unknown = summary.classifications.filter((c) => c.integrityLevel === IntegrityLevel.Unknown).length;
  if (unknown > 0) parts.push(`${unknown} 段完整性等级未设置`);
  return parts.length > 0 ? `以下信息待补充：${parts.join('、')}。` : null;
}

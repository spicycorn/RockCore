/**
 * 回次 → 岩体段归并器（逐行移植自 1.x RockMassSegmentBuilder，语义保持一致）。
 *
 * 规范里的评价单元是"岩体性质相对均一的连续段"，回次只是钻探取芯的采样单元，
 * 所以判完整程度之前必须先把相邻、指标相近的回次归并成岩体段：
 *   · 深度不连续（中间有被跳过/阻断的回次）→ 强制分段；
 *   · 岩质已知且不同 → 强制分段；
 *   · 平均间距相对差超过容差 → 另起一段；
 *   · 归并后仍不足最小段厚的薄段 → 并入间距最接近的相邻段。
 * 归并结果只用到原始量（进尺、段数、段长、岩质），不使用判级结果——
 * 顺序必须是"先归并、后判级"，否则边界回次的误判会被固化下来。
 */

/** 归并输入：一个回次（采样单元）的原始量 */
export interface RockMassRunInput {
  /** 调用方回填用的标识（导入预览用行序号） */
  key: number;
  /** 回次起点（m，含起始深度） */
  depthStart: number;
  /** 回次终点（m） */
  depthEnd: number;
  /** 岩心总段数 n = 正常段格数（破碎段不计） */
  totalPieces: number;
  /** ≥10cm 岩心段累计长度（cm） */
  totalLengthCm: number;
  /** 碎屑（<10cm 短段）块数；0 = 无碎屑 */
  fragCount?: number;
  /** 碎屑（<10cm 短段）累计长度（cm） */
  fragLengthCm?: number;
  /** true = 破碎段（现场记「长度，破碎」）：与正常段强制分界，等级直接取「破碎」 */
  broken?: boolean;
  /** F.0.2 岩质键值（RockType 编码）；0 或负数表示未填（未填时不作为强制分界） */
  rockTypeKey: number;
}

/** 四舍五入到 n 位小数（对齐 C# Math.Round(double, digits) 在工程数据上的输出语义） */
function round(value: number, digits: number): number {
  const f = 10 ** digits;
  return Math.round(value * f) / f;
}

/** 岩体段（完整程度分级单元）：由若干相邻、性质相近的回次归并而成 */
export class RockMassSegment {
  /** 组内回次（按深度先后） */
  readonly runs: RockMassRunInput[] = [];

  depthStart = 0;
  depthEnd = 0;
  /** 段厚（m）= 组内各回次进尺之和 */
  thicknessM = 0;
  /** 段内岩心总段数之和 Σn */
  totalPieces = 0;
  /** 段内 ≥10cm 岩心累计长度之和（cm） */
  totalLengthCm = 0;
  /** 段内碎屑块数之和 */
  fragCount = 0;
  /** 段内碎屑累计长度（cm） */
  fragLengthCm = 0;

  /** 回次数 */
  get runCount(): number {
    return this.runs.length;
  }

  /** 组内是否只有一个回次（未发生归并） */
  get isSingleRun(): boolean {
    return this.runs.length === 1;
  }

  /**
   * 段的平均间距（cm）= 段厚×100 ÷ Σn。
   * 用"总长 ÷ 总条数"的合并口径，而不是各回次间距的算术平均——后者会让
   * 短回次与长回次等权，样本量信息被丢掉。
   */
  get spacingCm(): number {
    return this.totalPieces > 0 ? round((this.thicknessM * 100) / this.totalPieces, 2) : 0;
  }

  /** 段的 RQD（%）= Σ合格段长 ÷ (段厚×100) × 100 */
  get rqd(): number {
    return this.thicknessM > 0 ? round((this.totalLengthCm / (this.thicknessM * 100)) * 100, 2) : 0;
  }

  /** 段的碎屑平均块径（cm）= Σ碎屑长 ÷ Σ碎屑块数；无碎屑 → NaN */
  get fragMeanSizeCm(): number {
    return this.fragCount > 0 ? round(this.fragLengthCm / this.fragCount, 2) : Number.NaN;
  }

  /** 组内是否含破碎段（含则整段直接判「破碎」，不再走间距判级） */
  get hasBroken(): boolean {
    return this.runs.some((r) => r.broken === true);
  }

  /** 组内回次的岩质键值（取第一个已填的；都没填返回 0） */
  get rockTypeKey(): number {
    return this.runs.find((r) => r.rockTypeKey > 0)?.rockTypeKey ?? 0;
  }

  add(run: RockMassRunInput): void {
    this.runs.push(run);
    this.recompute();
  }

  insertAtFront(run: RockMassRunInput): void {
    this.runs.unshift(run);
    this.recompute();
  }

  private recompute(): void {
    this.depthStart = Math.min(...this.runs.map((r) => r.depthStart));
    this.depthEnd = Math.max(...this.runs.map((r) => r.depthEnd));
    this.thicknessM = round(this.runs.reduce((s, r) => s + (r.depthEnd - r.depthStart), 0), 4);
    this.totalPieces = this.runs.reduce((s, r) => s + r.totalPieces, 0);
    this.totalLengthCm = round(this.runs.reduce((s, r) => s + r.totalLengthCm, 0), 2);
    this.fragCount = this.runs.reduce((s, r) => s + (r.fragCount ?? 0), 0);
    this.fragLengthCm = round(this.runs.reduce((s, r) => s + (r.fragLengthCm ?? 0), 0), 2);
  }
}

/** 回次的平均间距（cm）= 厚度×100 ÷ n；n=0 时为 0 */
function runSpacingCm(run: RockMassRunInput): number {
  const thickness = run.depthEnd - run.depthStart;
  return run.totalPieces > 0 ? (thickness * 100) / run.totalPieces : 0;
}

/** 深度连续性容差（m），吸收浮点累计误差 */
const DEPTH_EPSILON = 0.0015;

/**
 * 按深度顺序归并回次。
 * @param runs 回次集合（顺序不限，内部按起点排序）
 * @param minThicknessM 岩体段最小厚度（m）；<=0 时不做薄段吸收
 * @param spacingToleranceRatio 间距相对差容差（0~1）；>=1 时视为不因间距分段
 */
export function buildRockSegments(
  runs: readonly RockMassRunInput[],
  minThicknessM: number,
  spacingToleranceRatio: number,
): RockMassSegment[] {
  const groups: RockMassSegment[] = [];
  if (runs.length === 0) return groups;

  const sorted = [...runs].sort((a, b) => a.depthStart - b.depthStart || a.depthEnd - b.depthEnd);
  for (const run of sorted) {
    if (run.depthEnd - run.depthStart <= 0) continue; // 进尺非正的脏数据不参与归并

    const last = groups.length > 0 ? groups[groups.length - 1] : null;
    if (last != null && canMerge(last, run, spacingToleranceRatio)) {
      last.add(run);
    } else {
      const g = new RockMassSegment();
      g.add(run);
      groups.push(g);
    }
  }

  absorbThinGroups(groups, minThicknessM, spacingToleranceRatio);
  return groups;
}

/** 单个回次自成一段（关闭归并时的退化路径） */
export function buildOnePerRun(runs: readonly RockMassRunInput[]): RockMassSegment[] {
  const groups: RockMassSegment[] = [];
  const sorted = [...runs].sort((a, b) => a.depthStart - b.depthStart || a.depthEnd - b.depthEnd);
  for (const run of sorted) {
    if (run.depthEnd - run.depthStart <= 0) continue;
    const g = new RockMassSegment();
    g.add(run);
    groups.push(g);
  }
  return groups;
}

function canMerge(group: RockMassSegment, run: RockMassRunInput, tolerance: number): boolean {
  // 强制分界 1：深度不连续（中间有未入库的回次）
  if (!isContinuous(run.depthStart, group.depthEnd)) return false;

  // 强制分界 2：岩质已知且不同
  const groupRock = group.rockTypeKey;
  if (groupRock > 0 && run.rockTypeKey > 0 && groupRock !== run.rockTypeKey) return false;

  // 强制分界 3：破碎段与正常段不并（破碎段之间可以并）
  if ((run.broken === true) !== group.runs.some((r) => r.broken === true)) return false;

  // 软条件：该回次自身的平均间距与当前段平均间距的相对差须在容差内。
  // 必须用「回次 vs 段」而不是「合并后 vs 段」：后者会让已经很大的段对新的、
  // 性质明显不同的回次几乎不敏感（合并均值被摊薄），实测会把整孔吞成一段。
  if (tolerance >= 1) return true;

  const runSpacing = runSpacingCm(run);
  const current = group.spacingCm;
  const scale = Math.max(runSpacing, current);
  if (scale <= 0) return true; // 两侧都没有结构面（n=0），不因间距断开

  return Math.abs(runSpacing - current) <= tolerance * scale;
}

/** 两段的平均间距是否相容（判据与 canMerge 一致）；任一侧 n=0 时不设限 */
function spacingCompatible(a: RockMassSegment, b: RockMassSegment, tolerance: number): boolean {
  if (tolerance >= 1) return true;
  const scale = Math.max(a.spacingCm, b.spacingCm);
  if (scale <= 0) return true;
  return Math.abs(a.spacingCm - b.spacingCm) <= tolerance * scale;
}

/**
 * 薄段吸收：把不足最小厚度、且与相邻段间距相容的段并入"间距最接近"的相邻段，
 * 循环到没有可吸收的薄段。每轮至少减少一个段，必然终止。
 *
 * 间距相容判据与 canMerge 一致：|ΔS| ≤ tolerance×max(S)。
 * 这是破碎带单独成段的前提——若薄段无条件被吸收，碎屑段会被相邻的正常岩心吞掉、
 * 重新摊平，拆段就白做了；间距明显不同的薄段（真薄破碎带）保留为独立薄段。
 */
function absorbThinGroups(groups: RockMassSegment[], minThicknessM: number, tolerance: number): void {
  if (minThicknessM <= 0) return;

  let changed = true;
  while (changed) {
    changed = false;
    for (let i = 0; i < groups.length; i++) {
      const g = groups[i];
      if (g.thicknessM >= minThicknessM - DEPTH_EPSILON) continue;

      const prev = i > 0 ? groups[i - 1] : null;
      const next = i < groups.length - 1 ? groups[i + 1] : null;

      const canPrev = prev != null && isContinuous(g.depthStart, prev.depthEnd)
        && rockCompatible(g, prev) && spacingCompatible(g, prev, tolerance);
      const canNext = next != null && isContinuous(next.depthStart, g.depthEnd)
        && rockCompatible(g, next) && spacingCompatible(g, next, tolerance);
      if (!canPrev && !canNext) continue;

      // 选间距更接近的一侧；差距在容差内时优先并到更厚的一侧（避免薄并薄来回抖动）
      let target: RockMassSegment;
      if (canPrev && canNext) {
        const dPrev = Math.abs(prev!.spacingCm - g.spacingCm);
        const dNext = Math.abs(next!.spacingCm - g.spacingCm);
        const scale = Math.max(Math.max(dPrev, dNext), 1e-9);
        target = Math.abs(dPrev - dNext) <= 0.25 * scale
          ? (prev!.thicknessM >= next!.thicknessM ? prev! : next!)
          : (dPrev < dNext ? prev! : next!);
      } else {
        target = canPrev ? prev! : next!;
      }

      if (target === prev) {
        // 逆序前插，保持组内回次仍按深度先后排列
        for (const run of [...g.runs].reverse()) target.insertAtFront(run);
      } else {
        for (const run of g.runs) target.add(run);
      }

      groups.splice(i, 1);
      changed = true;
      break; // 组索引已变，重新扫描
    }
  }
}

/** 深度是否首尾相接（容差吸收浮点累计误差） */
function isContinuous(start: number, end: number): boolean {
  return Math.abs(start - end) <= DEPTH_EPSILON;
}

/** 两个段的岩质是否可归并（任一侧未填时不设限） */
function rockCompatible(a: RockMassSegment, b: RockMassSegment): boolean {
  const ra = a.rockTypeKey;
  const rb = b.rockTypeKey;
  return ra <= 0 || rb <= 0 || ra === rb;
}

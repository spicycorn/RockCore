using System;
using System.Collections.Generic;

namespace RockCore.Core.Models;

/// <summary>
/// 围岩规范配置文件模型。
/// 包含了所有可由用户自定义的判定依据、区间限制和词汇映射。
/// </summary>
public class RockSpecificationConfig
{
    /// <summary>
    /// 词汇映射表：用于定义岩质类型、完整性等级等的显示名称
    /// Key: 枚举的名称或值, Value: 用户自定义的显示文本
    /// </summary>
    public Dictionary<string, string> Terminology { get; set; } = new();

    /// <summary>
    /// 数值阈值限制
    /// </summary>
    public Thresholds ConfigThresholds { get; set; } = new();

    /// <summary>
    /// 岩体结构映射关系：(岩质类型, 完整性等级) -> 可选的岩体结构类型列表
    /// Key 格式: "RockType_IntegrityLevel" (例如 "HardRock_Intact")
    /// </summary>
    public Dictionary<string, List<int>> StructureMappings { get; set; } = new();

    /// <summary>
    /// 岩体完整程度划分表（规范表 F.0.4），按完整性等级列出结构面间距（判级依据）、
    /// 结构面发育组数与发育程度（后两者为地质描述字段，自动判级不使用，见 IntegrityCriteriaEngine）。
    /// </summary>
    public List<IntegrityLevelCriterion> IntegrityLevelCriteria { get; set; } = new();

    public class Thresholds
    {
        /// <summary>
        /// 洞轴线与岩层走向夹角判定阈值（默认 30 度）
        /// </summary>
        public double CaveAxisAngleThreshold { get; set; } = 30.0;

        /// <summary>
        /// 是否把相邻回次归并成「岩体段」后再判完整程度（默认 true）。
        /// 规范的评价单元是"岩体性质相对均一的连续段"，回次只是采样单元；
        /// 置为 false 时退回"一个回次 = 一个完整性分段"的旧行为。
        /// </summary>
        public bool MergeRunsIntoRockSegments { get; set; } = true;

        /// <summary>
        /// 岩体段最小厚度（m，默认 1.0）。归并后仍不足该厚度的薄段并入间距最接近的相邻段。
        /// </summary>
        public double RockSegmentMinThicknessM { get; set; } = 1.0;

        /// <summary>
        /// 归并容差：相邻回次的平均间距与当前段合并间距的相对差超过该比例时另起一段（默认 0.5，即 50%）。
        /// </summary>
        public double RockSegmentSpacingToleranceRatio { get; set; } = 0.5;
    }
}

/// <summary>
/// 单个完整性等级的判定指标，对应规范表 F.0.4 的一行。
/// </summary>
public class IntegrityLevelCriterion
{
    /// <summary>完整性等级键名，如 Intact / RelativelyIntact / Poor / RelativelyBroken / Broken</summary>
    public string LevelKey { get; set; } = string.Empty;

    /// <summary>
    /// 结构面发育组数，如 "1~2"。
    /// 地质描述字段：自动判级<b>不</b>使用它（回次取芯算不出节理组数），仅在规范设置中供人工记录。
    /// </summary>
    public string JointSetCount { get; set; } = string.Empty;

    /// <summary>结构面间距（cm），如 ">95"。自动判级的唯一依据。</summary>
    public string JointSpacing { get; set; } = string.Empty;

    /// <summary>结构面发育程度，如 "不发育"。地质描述字段，不参与判级。</summary>
    public string JointDevelopment { get; set; } = string.Empty;
}

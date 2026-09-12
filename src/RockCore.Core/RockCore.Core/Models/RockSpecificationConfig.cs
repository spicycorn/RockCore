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
    /// 岩体完整程度划分表（规范表 F.0.4），按完整性等级列出结构面发育组数、间距和发育程度。
    /// </summary>
    public List<IntegrityLevelCriterion> IntegrityLevelCriteria { get; set; } = new();

    public class Thresholds
    {
        /// <summary>
        /// 洞轴线与岩层走向夹角判定阈值（默认 30 度）
        /// </summary>
        public double CaveAxisAngleThreshold { get; set; } = 30.0;
    }
}

/// <summary>
/// 单个完整性等级的判定指标，对应规范表 F.0.4 的一列。
/// </summary>
public class IntegrityLevelCriterion
{
    /// <summary>完整性等级键名，如 Intact / RelativelyIntact / Poor / RelativelyBroken / Broken</summary>
    public string LevelKey { get; set; } = string.Empty;

    /// <summary>结构面发育组数，如 "1~2"</summary>
    public string JointSetCount { get; set; } = string.Empty;

    /// <summary>结构面间距（cm），如 ">100"</summary>
    public string JointSpacing { get; set; } = string.Empty;

    /// <summary>结构面发育程度，如 "不发育"</summary>
    public string JointDevelopment { get; set; } = string.Empty;
}

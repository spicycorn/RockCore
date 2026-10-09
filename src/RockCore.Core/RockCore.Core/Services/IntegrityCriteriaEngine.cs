using System.Globalization;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Core.Services;

/// <summary>
/// 表 F.0.4「岩体完整程度划分」判定引擎。
/// 判定依据为可配置的 <see cref="IntegrityLevelCriterion"/> 表（规范设置中可编辑），
/// 配置为空时回退到内置默认表。
///
/// 自动判级（Excel 导入）走 <see cref="TryMatchBySpacing"/>：<b>只按结构面间距查表</b>，
/// 「结构面发育组数」不参与匹配——组数是地质描述量，回次取芯数据算不出它。
/// <see cref="TryMatch"/> 保留给组数确已知的场景。
///
/// 区间记法（与规范表写法一致，大小写不敏感）：
///   结构面发育组数（整数，仅 TryMatch 使用）：
///     "1~2" → 1 ≤ J ≤ 2
///     "&gt;3"  → J &gt; 3
///     "—" / "——" / 空 → 不限制
///   结构面间距（cm，实数）：
///     "50~95" → 50 &lt; S ≤ 95（下限开、上限闭，与规范表 "&gt;50~100" 类写法一致）
///     "&gt;95" → S &gt; 95
///     "&lt;2"  → S &lt; 2
///     "≤10" / "&lt;=10" → S ≤ 10
///     "—" / 空 → 不限制
///
/// 判定顺序：按表中行的先后顺序（第 1 行最严格），首次匹配即返回。
/// </summary>
public static class IntegrityCriteriaEngine
{
    /// <summary>
    /// 内置默认判定表（表 F.0.4）。
    ///
    /// 【逻辑修正说明】自动判级**只使用「结构面间距」**，「结构面发育组数」不再参与匹配
    /// （见 <see cref="TryMatchBySpacing"/>）。原因：组数是岩体的地质描述量（节理**组数**，
    /// 取值 1~2 / 2~3 / >3），无法由回次取芯数据算出；旧实现把"回次内节理条数 n−1"当作组数代入，
    /// 量纲差 1~2 个数量级，导致 J&gt;3 且 S&gt;10 的组合在表里一行都匹配不上、只能走兜底分支。
    /// 表中的**间距分档与等级映射数值保持原样未动**；「组数」列保留为地质描述字段（默认"—"）。
    ///
    /// 行序 = 优先级（首次匹配即返回）：破碎行置顶，保持"间距&lt;2cm → 破碎"的最高优先级。
    /// 用户可在「规范设置 → 岩体完整程度划分表」中自行修改取值与行序。
    /// </summary>
    public static List<IntegrityLevelCriterion> GetDefaultCriteria()
    {
        return new List<IntegrityLevelCriterion>
        {
            new() { LevelKey = "Broken",           JointSetCount = "—", JointSpacing = "<2"   },
            new() { LevelKey = "RelativelyBroken", JointSetCount = "—", JointSpacing = "2~10" },
            new() { LevelKey = "Poor",             JointSetCount = "—", JointSpacing = "10~30" },
            new() { LevelKey = "RelativelyIntact", JointSetCount = "—", JointSpacing = "30~50" },
            new() { LevelKey = "RelativelyIntact", JointSetCount = "—", JointSpacing = "50~95" },
            new() { LevelKey = "Intact",           JointSetCount = "—", JointSpacing = ">95"  }
        };
    }

    /// <summary>
    /// 仅按「结构面间距」查找首次匹配行（按行序，第 1 行优先）——自动判级的唯一入口。
    /// 刻意忽略 <see cref="IntegrityLevelCriterion.JointSetCount"/>：组数不是回次能测出的量，
    /// 让它参与匹配只会让表形同虚设（详见 <see cref="GetDefaultCriteria"/> 的说明）。
    /// </summary>
    public static IntegrityLevelCriterion? TryMatchBySpacing(
        IReadOnlyList<IntegrityLevelCriterion>? criteria,
        double spacingCm)
    {
        if (criteria == null || criteria.Count == 0)
            return null;

        foreach (var c in criteria)
        {
            if (!ParseSpacingRange(c.JointSpacing, out var sMin, out var sMinInclusive,
                    out var sMax, out var sMaxInclusive, out _))
                continue;
            if (sMinInclusive ? spacingCm < sMin : spacingCm <= sMin) continue;
            if (sMaxInclusive ? spacingCm > sMax : spacingCm >= sMax) continue;
            return c;
        }
        return null;
    }

    /// <summary>
    /// 在判定表中查找首次匹配行（按行序，第 1 行优先）。
    /// 保留给"组数确已知"的场景（如人工地质描述、历史图像分析路径）；
    /// Excel 导入的自动判级请走 <see cref="TryMatchBySpacing"/>。
    /// </summary>
    public static IntegrityLevelCriterion? TryMatch(
        IReadOnlyList<IntegrityLevelCriterion>? criteria,
        int jointCount,
        double spacingCm)
    {
        if (criteria == null || criteria.Count == 0)
            return null;

        foreach (var c in criteria)
        {
            if (Matches(c, jointCount, spacingCm))
                return c;
        }
        return null;
    }

    private static bool Matches(IntegrityLevelCriterion c, int jointCount, double spacingCm)
    {
        // 组数：不限制时 min=0、max=int.MaxValue，比较式自然恒真
        if (!ParseCountRange(c.JointSetCount, out var jMin, out var jMax, out _))
            return false;
        if (jointCount < jMin || jointCount > jMax)
            return false;

        // 间距：不限制时 min=-∞、max=+∞（均闭），比较式自然恒真
        if (!ParseSpacingRange(c.JointSpacing, out var sMin, out var sMinInclusive, out var sMax, out var sMaxInclusive, out _))
            return false;
        if ((sMinInclusive ? spacingCm < sMin : spacingCm <= sMin)
            || (sMaxInclusive ? spacingCm > sMax : spacingCm >= sMax))
            return false;

        return true;
    }

    /// <summary>
    /// 解析整数区间（结构面发育组数）。
    /// 支持 "A~B"（闭区间）、">N"、"≥N"、"—"/空（不限制）。
    /// 返回 false 表示该表达式无法解析（此时该行不参与匹配）。
    /// </summary>
    public static bool ParseCountRange(string? text, out int min, out int max, out bool any)
    {
        min = 0; max = int.MaxValue; any = false;

        var s = (text ?? string.Empty).Trim();
        if (s.Length == 0 || s is "—" or "——" or "-" or "不限")
        {
            any = true;
            return true;
        }

        // 替换全角字符
        s = s.Replace('＞', '>');

        if (s.StartsWith('>'))
        {
            if (int.TryParse(s.AsSpan(1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                min = n + 1; // J > n  ⇔  J ≥ n+1（整数）
                return true;
            }
            return false;
        }
        if (s.StartsWith('≥'))
        {
            if (int.TryParse(s.AsSpan(1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n2))
            {
                min = n2;
                return true;
            }
            return false;
        }

        int tilde = s.IndexOfAny(new[] { '~', '～' });
        if (tilde >= 0)
        {
            bool okA = int.TryParse(s.AsSpan(0, tilde).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a);
            bool okB = int.TryParse(s.AsSpan(tilde + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b);
            if (okA && okB)
            {
                min = Math.Min(a, b);
                max = Math.Max(a, b);
                return true;
            }
            return false;
        }

        // 单值：视为精确匹配
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exact))
        {
            min = max = exact;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 解析实数区间（结构面间距 cm）。
    /// 支持 "A~B"（A 开 B 闭）、"&gt;N"、"&lt;N"、"≤N"/"&lt;=N"、"≥N"/"&gt;=N"、"—"/空（不限制）。
    /// 返回 false 表示该表达式无法解析（此时该行不参与匹配）。
    /// </summary>
    public static bool ParseSpacingRange(
        string? text,
        out double min, out bool minInclusive,
        out double max, out bool maxInclusive,
        out bool any)
    {
        min = double.NegativeInfinity; max = double.PositiveInfinity;
        minInclusive = true; maxInclusive = true; any = false;

        var s = (text ?? string.Empty).Trim();
        if (s.Length == 0 || s is "—" or "——" or "-" or "不限")
        {
            any = true;
            return true;
        }

        s = s.Replace('＞', '>').Replace('＜', '<').Replace('＝', '=');

        if (s.StartsWith('>'))
        {
            // ">=" → ≥；">" → 严格大于
            bool inclusive = s.Length > 1 && s[1] == '=';
            if (TryParseDouble(s.AsSpan(inclusive ? 2 : 1).Trim().ToString(), out var n))
            {
                min = n; minInclusive = inclusive;
                return true;
            }
            return false;
        }
        if (s.StartsWith('<'))
        {
            // "<=" → ≤；"<" → 严格小于
            bool inclusive = s.Length > 1 && s[1] == '=';
            if (TryParseDouble(s.AsSpan(inclusive ? 2 : 1).Trim().ToString(), out var n2))
            {
                max = n2; maxInclusive = inclusive;
                return true;
            }
            return false;
        }
        if (s.StartsWith('≤'))
        {
            if (TryParseDouble(s.AsSpan(1).Trim().ToString(), out var n3))
            {
                max = n3; maxInclusive = true;
                return true;
            }
            return false;
        }
        if (s.StartsWith('≥'))
        {
            if (TryParseDouble(s.AsSpan(1).Trim().ToString(), out var n4))
            {
                min = n4; minInclusive = true;
                return true;
            }
            return false;
        }

        int tilde = s.IndexOfAny(new[] { '~', '～' });
        if (tilde >= 0)
        {
            bool okA = TryParseDouble(s.AsSpan(0, tilde).Trim().ToString(), out var a);
            bool okB = TryParseDouble(s.AsSpan(tilde + 1).Trim().ToString(), out var b);
            if (okA && okB)
            {
                // A~B：下限开、上限闭（与规范表 ">50~100" 类写法一致）
                if (a <= b)
                {
                    min = a; minInclusive = false;
                    max = b; maxInclusive = true;
                }
                else
                {
                    min = b; minInclusive = false;
                    max = a; maxInclusive = true;
                }
                return true;
            }
            return false;
        }

        // 单值：视为精确匹配
        if (TryParseDouble(s, out var exact))
        {
            min = max = exact;
            minInclusive = maxInclusive = true;
            return true;
        }

        return false;
    }

    private static bool TryParseDouble(string s, out double value)
    {
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 解析完整性等级键名。无法识别时返回 Unknown。
    /// </summary>
    public static IntegrityLevel ParseLevel(string? levelKey)
    {
        return (levelKey ?? string.Empty).Trim() switch
        {
            "Intact" => IntegrityLevel.Intact,
            "RelativelyIntact" => IntegrityLevel.RelativelyIntact,
            "Poor" => IntegrityLevel.Poor,
            "RelativelyBroken" => IntegrityLevel.RelativelyBroken,
            "Broken" => IntegrityLevel.Broken,
            "UserOverride" => IntegrityLevel.UserOverride,
            _ => IntegrityLevel.Unknown
        };
    }
}

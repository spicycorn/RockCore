using System.Globalization;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Core.Services;

/// <summary>
/// 表 F.0.4「岩体完整程度划分」判定引擎。
/// 判定依据为可配置的 <see cref="IntegrityLevelCriterion"/> 表（规范设置中可编辑），
/// 配置为空时回退到内置默认表（与历史版本行为一致，不改变默认判定依据）。
///
/// 区间记法（与规范表写法一致，大小写不敏感）：
///   结构面发育组数（整数）：
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
    /// 内置默认判定表。与历史硬编码判定逻辑一致（完整边界 95cm 的项目经验调整值）。
    /// 行序 = 优先级（首次匹配即返回）：第 1 行"间距&lt;2cm（无序）→破碎"优先级最高，
    /// 与历史逻辑中"先判 S&lt;2 → 破碎"的行为完全一致。
    /// 用户可在「规范设置 → 岩体完整程度划分表」中自行修改取值与行序。
    /// </summary>
    public static List<IntegrityLevelCriterion> GetDefaultCriteria()
    {
        return new List<IntegrityLevelCriterion>
        {
            new() { LevelKey = "Broken",           JointSetCount = "—",   JointSpacing = "<2",   JointDevelopment = "——" },
            new() { LevelKey = "Intact",           JointSetCount = "1~2", JointSpacing = ">95",  JointDevelopment = "不发育" },
            new() { LevelKey = "RelativelyIntact", JointSetCount = "1~2", JointSpacing = "50~95",JointDevelopment = "轻度发育" },
            new() { LevelKey = "RelativelyIntact", JointSetCount = "2~3", JointSpacing = "30~50",JointDevelopment = "中等发育" },
            new() { LevelKey = "Poor",             JointSetCount = "2~3", JointSpacing = "10~30",JointDevelopment = "较发育" },
            new() { LevelKey = "Poor",             JointSetCount = "2~3", JointSpacing = "≤10",  JointDevelopment = "发育" },
            new() { LevelKey = "RelativelyBroken", JointSetCount = ">3",  JointSpacing = "≤10",  JointDevelopment = "很发育" }
        };
    }

    /// <summary>
    /// 在判定表中查找首次匹配行（按行序，第 1 行优先）。
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

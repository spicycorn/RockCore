using RockCore.Core.Enums;

namespace RockCore.Core.Services;

/// <summary>
/// 围岩类别文本映射的单一实现，供统计、摘要等各处复用，避免 Roman 数字映射散落多处。
/// 使用 Unicode 罗马数字（Ⅰ Ⅱ Ⅲ Ⅳ Ⅴ）以符合规范表书写习惯。
/// </summary>
public static class RockClassText
{
    /// <summary>
    /// 返回单个罗马数字，如 Ⅰ / Ⅱ / Ⅲ / Ⅳ / Ⅴ。
    /// </summary>
    public static string Numeral(RockClass rockClass)
    {
        return rockClass switch
        {
            RockClass.I => "Ⅰ",
            RockClass.II => "Ⅱ",
            RockClass.III => "Ⅲ",
            RockClass.IV => "Ⅳ",
            RockClass.V => "Ⅴ",
            _ => rockClass.ToString()
        };
    }

    /// <summary>
    /// 返回带"类"后缀的文本，如 "Ⅰ类"。
    /// </summary>
    public static string WithSuffix(RockClass rockClass) => $"{Numeral(rockClass)}类";
}

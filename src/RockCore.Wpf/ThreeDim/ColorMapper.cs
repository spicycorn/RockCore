using System.Windows.Media;
using RockCore.Core.Enums;

namespace RockCore.Wpf.ThreeDim;

public static class ColorMapper
{
    public static Color GetColorByRockClass(RockClass rockClass)
    {
        return rockClass switch
        {
            RockClass.I => Color.FromArgb(255, 70, 130, 180),   // 钢蓝 - I类最优
            RockClass.II => Color.FromArgb(255, 60, 179, 113),  // 中等绿
            RockClass.III => Color.FromArgb(255, 238, 238, 0),  // 黄色 - III类中间
            RockClass.IV => Color.FromArgb(255, 255, 165, 0),   // 橙色
            RockClass.V => Color.FromArgb(255, 220, 20, 60),    // 深红 - V类最差
            _ => Color.FromArgb(255, 128, 128, 128)             // 灰色默认
        };
    }

    public static Color GetColorByIntegrityLevel(IntegrityLevel level)
    {
        return level switch
        {
            IntegrityLevel.Intact => Color.FromArgb(255, 34, 139, 34),      // 深绿 - 完整
            IntegrityLevel.RelativelyIntact => Color.FromArgb(255, 60, 179, 113), // 中等绿
            IntegrityLevel.Poor => Color.FromArgb(255, 144, 238, 144),     // 浅绿 - 完整性差
            IntegrityLevel.RelativelyBroken => Color.FromArgb(255, 255, 200, 0),    // 黄橙 - 较破碎
            IntegrityLevel.Broken => Color.FromArgb(255, 255, 100, 0),     // 橙色 - 破碎
            _ => Color.FromArgb(255, 180, 180, 180)                        // 灰色默认
        };
    }

    public static Color GetColorByRockType(RockType rockType)
    {
        return rockType switch
        {
            RockType.HardRock => Color.FromArgb(255, 100, 149, 237),   // 天蓝 - 硬质岩
            RockType.SoftRock => Color.FromArgb(255, 210, 180, 140),   // 浅褐 - 软质岩
            _ => Color.FromArgb(255, 160, 160, 160)                    // 灰色默认
        };
    }

    public static SolidColorBrush ToBrush(Color color)
    {
        return new SolidColorBrush(color);
    }

    public static System.Windows.Media.Color ToMediaColor(System.Drawing.Color color)
    {
        return System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }
}

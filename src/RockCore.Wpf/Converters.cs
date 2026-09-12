using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using RockCore.Core.Enums;

namespace RockCore.Wpf;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isEmpty = value == null || (value is string str && string.IsNullOrEmpty(str));

        // 处理集合类型：如果是集合且为空，视为 empty
        if (!isEmpty && value is IEnumerable enumerable)
        {
            IEnumerator enumerator = enumerable.GetEnumerator();
            isEmpty = !enumerator.MoveNext();
            if (enumerator is IDisposable disposable)
                disposable.Dispose();
        }

        bool inverse = parameter as string == "inverse";
        if (inverse)
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NotNullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool boolVal && boolVal;
        string? param = parameter as string;
        if (param == "inverse") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// 计算深度差值（用于显示信息段长度）
public class DepthDiffConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double depthEnd && parameter is string startParam && double.TryParse(startParam, out double depthStart))
        {
            return (depthEnd - depthStart).ToString("F2");
        }
        return "0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// 辅助类：提供岩质类型下拉选项
public static class RockTypeHelper
{
    public static RockType[] Values { get; } = Enum.GetValues<RockType>()
        .Where(v => v != RockType.NotSet).ToArray();
}

// 辅助类：提供完整性等级下拉选项
public static class IntegrityLevelHelper
{
    public static IntegrityLevel[] Values { get; } = Enum.GetValues<IntegrityLevel>()
        .Where(v => v != IntegrityLevel.Unknown).ToArray();
}

// 辅助类：提供规范设置 F.0.4 表中的完整性等级键选项
public static class IntegrityLevelKeyHelper
{
    public static IntegrityLevel[] Values { get; } = new[]
    {
        IntegrityLevel.Intact,
        IntegrityLevel.RelativelyIntact,
        IntegrityLevel.Poor,
        IntegrityLevel.RelativelyBroken,
        IntegrityLevel.Broken
    };
}

// 辅助类：提供岩体结构类型下拉选项（按岩质类型分组，严格对应规范表 F.0.2）
public static class RockStructureTypeHelper
{
    public static RockStructureType[] Values { get; } = Enum.GetValues<RockStructureType>()
        .Where(v => v != RockStructureType.NotSet).ToArray();

    // 硬质岩对应的岩体结构类型（10种，按规范表顺序排列）
    public static RockStructureType[] HardRockStructures { get; } = new[]
    {
        RockStructureType.Massive,            // 整体状或巨厚层状结构
        RockStructureType.Blocky,             // 块状结构
        RockStructureType.SubBlocky,          // 次块状结构
        RockStructureType.ThickLayered,       // 厚层状或中厚层状结构
        RockStructureType.Interbedded,        // 互层状结构
        RockStructureType.ThinLayered,        // 薄层状结构
        RockStructureType.Mosaic,             // 镶嵌结构
        RockStructureType.BlockyFractured,    // 块裂结构
        RockStructureType.Cataclastic,        // 碎裂结构
        RockStructureType.GranularHard        // 碎块状或碎屑状结构
    };

    // 软质岩对应的岩体结构类型（6种，按规范表顺序排列）
    public static RockStructureType[] SoftRockStructures { get; } = new[]
    {
        RockStructureType.Massive,            // 整体状或巨厚层状结构
        RockStructureType.BlockyOrSubBlocky,  // 块状或次块状结构
        RockStructureType.ThickOrInterbedded, // 厚层、中厚层或互层状结构
        RockStructureType.ThinOrBlockyFractured, // 薄层状或块裂结构
        RockStructureType.Cataclastic,        // 碎裂结构
        RockStructureType.GranularSoft        // 碎块状或碎屑状散体结构
    };

    // 根据岩质类型获取对应的岩体结构类型数组
    public static RockStructureType[] GetStructuresForRockType(RockType rockType)
    {
        return rockType switch
        {
            RockType.HardRock => HardRockStructures,
            RockType.SoftRock => SoftRockStructures,
            _ => Values
        };
    }

    // 根据岩质类型 + 完整性等级 获取对应的岩体结构类型数组（严格对应规范表F.0.2）
    public static RockStructureType[] GetStructuresForRockTypeAndLevel(RockType rockType, IntegrityLevel level)
    {
        if (rockType == RockType.HardRock)
        {
            return level switch
            {
                IntegrityLevel.Intact => new[] { RockStructureType.Massive },
                IntegrityLevel.RelativelyIntact => new[]
                {
                    RockStructureType.Blocky,
                    RockStructureType.SubBlocky,
                    RockStructureType.ThickLayered,
                    RockStructureType.Interbedded
                },
                IntegrityLevel.Poor => new[]
                {
                    RockStructureType.ThinLayered,
                    RockStructureType.Mosaic,
                    RockStructureType.BlockyFractured
                },
                IntegrityLevel.RelativelyBroken => new[] { RockStructureType.Cataclastic },
                IntegrityLevel.Broken => new[] { RockStructureType.GranularHard },
                _ => HardRockStructures
            };
        }
        else if (rockType == RockType.SoftRock)
        {
            return level switch
            {
                IntegrityLevel.Intact => new[] { RockStructureType.Massive },
                IntegrityLevel.RelativelyIntact => new[]
                {
                    RockStructureType.BlockyOrSubBlocky,
                    RockStructureType.ThickOrInterbedded
                },
                IntegrityLevel.Poor => new[] { RockStructureType.ThinOrBlockyFractured },
                IntegrityLevel.RelativelyBroken => new[] { RockStructureType.Cataclastic },
                IntegrityLevel.Broken => new[] { RockStructureType.GranularSoft },
                _ => SoftRockStructures
            };
        }
        return Values;
    }
}

// 枚举描述转换器：将枚举值转换为其 [Description] 特性的中文文本
public class EnumDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return string.Empty;

        var type = value.GetType();
        if (!type.IsEnum) return value.ToString() ?? string.Empty;

        var name = Enum.GetName(type, value);
        if (name == null) return value.ToString() ?? string.Empty;

        var field = type.GetField(name);
        if (field == null) return value.ToString() ?? string.Empty;

        var attr = field.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

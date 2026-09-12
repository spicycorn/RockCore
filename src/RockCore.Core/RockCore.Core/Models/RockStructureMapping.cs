using RockCore.Core.Enums;
using RockCore.Core.Interfaces;

namespace RockCore.Core.Models;

/// <summary>
/// 岩体结构映射辅助类。
/// 通过静态委托从外部注入 ISpecificationService，避免 Core 层依赖 Infrastructure。
/// </summary>
public static class RockStructureMapping
{
    private static Func<ISpecificationService?> _serviceProvider = () => null;

    /// <summary>
    /// 设置服务提供者委托，必须在程序启动时调用一次
    /// </summary>
    public static void Initialize(Func<ISpecificationService?> serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 根据岩质类型和完整性等级获取对应的岩体结构类型列表
    /// </summary>
    public static RockStructureType[] GetStructures(RockType rockType, IntegrityLevel level)
    {
        var service = _serviceProvider?.Invoke();
        if (service != null)
        {
            return service.GetAvailableStructures(rockType, level);
        }

        // 极端 fallback：如果服务未初始化，返回基础列表确保不崩溃
        return rockType == RockType.HardRock ? 
            new[] { RockStructureType.Massive, RockStructureType.Blocky, RockStructureType.SubBlocky, RockStructureType.ThickLayered, RockStructureType.Interbedded, RockStructureType.ThinLayered, RockStructureType.Mosaic, RockStructureType.BlockyFractured, RockStructureType.Cataclastic, RockStructureType.GranularHard } :
            new[] { RockStructureType.Massive, RockStructureType.BlockyOrSubBlocky, RockStructureType.ThickOrInterbedded, RockStructureType.ThinOrBlockyFractured, RockStructureType.Cataclastic, RockStructureType.GranularSoft };
    }

    public static RockStructureType[] AllStructures { get; } = Enum.GetValues<RockStructureType>()
        .Where(v => v != RockStructureType.NotSet).ToArray();
}
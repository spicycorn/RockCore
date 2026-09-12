using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

public interface ISpecificationService
{
    RockSpecificationConfig CurrentConfig { get; }
    void SaveConfig(RockSpecificationConfig config);
    RockStructureType[] GetAvailableStructures(RockType rockType, IntegrityLevel level);
    double GetCaveAxisThreshold();
}
using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

public interface IStructuralPlaneRepository
{
    Task<List<StructuralPlane>> GetByCorePhotoIdAsync(int corePhotoId);
    Task<int> AddAsync(StructuralPlane plane);
    Task AddRangeAsync(IEnumerable<StructuralPlane> planes);
    Task UpdateAsync(StructuralPlane plane);
    Task DeleteAsync(int id);
    Task DeleteByCorePhotoIdAsync(int corePhotoId);
}

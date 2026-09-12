using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

public interface ICorePhotoRepository
{
    Task<IEnumerable<CorePhoto>> GetByBoreholeIdAsync(int boreholeId);
    Task<CorePhoto?> GetByIdAsync(int id);
    Task<CorePhoto> AddAsync(CorePhoto corePhoto);
    Task AddRangeAsync(IEnumerable<CorePhoto> corePhotos);
    Task UpdateAsync(CorePhoto corePhoto);
    Task DeleteAsync(int id);
    Task DeleteByBoreholeIdAsync(int boreholeId);
    Task UpdateRangeAsync(IEnumerable<CorePhoto> corePhotos);
    Task<double?> GetMaxDepthEndAsync(int boreholeId);
}


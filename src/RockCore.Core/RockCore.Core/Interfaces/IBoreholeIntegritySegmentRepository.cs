using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

public interface IBoreholeIntegritySegmentRepository
{
    Task<IEnumerable<BoreholeIntegritySegment>> GetByBoreholeIdAsync(int boreholeId);
    Task<BoreholeIntegritySegment?> GetByIdAsync(int id);
    Task<BoreholeIntegritySegment> AddAsync(BoreholeIntegritySegment segment);
    Task AddRangeAsync(IEnumerable<BoreholeIntegritySegment> segments);
    Task UpdateAsync(BoreholeIntegritySegment segment);
    Task UpdateRangeAsync(IEnumerable<BoreholeIntegritySegment> segments);
    Task DeleteAsync(int id);
    Task DeleteByBoreholeIdAsync(int boreholeId);
}

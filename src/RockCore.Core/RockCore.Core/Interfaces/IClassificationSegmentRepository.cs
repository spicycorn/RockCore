using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

public interface IClassificationSegmentRepository
{
    Task<IEnumerable<ClassificationSegment>> GetByBoreholeIdAsync(int boreholeId);
    Task<ClassificationSegment?> GetByIdAsync(int id);
    Task<ClassificationSegment> AddAsync(ClassificationSegment segment);
    Task AddRangeAsync(IEnumerable<ClassificationSegment> segments);
    Task UpdateAsync(ClassificationSegment segment);
    Task UpdateRangeAsync(IEnumerable<ClassificationSegment> segments);
    Task DeleteAsync(int id);
    Task DeleteByBoreholeIdAsync(int boreholeId);
    Task<IEnumerable<ClassificationSegment>> GetByProjectIdAsync(int projectId);
}

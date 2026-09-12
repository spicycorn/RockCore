using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

public interface IAnalysisMetricsRepository
{
    Task<List<AnalysisMetrics>> GetByCorePhotoIdAsync(int corePhotoId);
    Task AddAsync(AnalysisMetrics metric);
    Task AddRangeAsync(IEnumerable<AnalysisMetrics> metrics);
    Task DeleteByCorePhotoIdAsync(int corePhotoId);
}

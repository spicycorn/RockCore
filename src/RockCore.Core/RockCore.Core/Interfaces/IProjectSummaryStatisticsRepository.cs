using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

public interface IProjectSummaryStatisticsRepository
{
    Task<IEnumerable<ProjectSummaryStatistics>> GetAllAsync();
    Task<ProjectSummaryStatistics?> GetByProjectIdAsync(int projectId);
    Task<ProjectSummaryStatistics> AddAsync(ProjectSummaryStatistics statistics);
    Task UpdateAsync(ProjectSummaryStatistics statistics);
    Task DeleteAsync(int id);
    Task DeleteByProjectIdAsync(int projectId);
}

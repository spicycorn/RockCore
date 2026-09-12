using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

public interface IBoreholeRepository
{
    Task<IEnumerable<Borehole>> GetByProjectIdAsync(int projectId);
    Task<Borehole?> GetByIdAsync(int id);
    Task<Borehole> AddAsync(Borehole borehole);
    Task UpdateAsync(Borehole borehole);
    Task DeleteAsync(int id);
}

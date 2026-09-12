using Microsoft.Data.Sqlite;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Repositories;

public class ProjectSummaryStatisticsRepository : IProjectSummaryStatisticsRepository
{
    private readonly RockCoreDbContext _context;

    public ProjectSummaryStatisticsRepository(RockCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ProjectSummaryStatistics>> GetAllAsync()
    {
        var statistics = new List<ProjectSummaryStatistics>();
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ProjectSummaryStatistics ORDER BY CreatedAt DESC";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statistics.Add(MapToStatistics(reader));
        }
        return statistics;
    }

    public async Task<ProjectSummaryStatistics?> GetByProjectIdAsync(int projectId)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ProjectSummaryStatistics WHERE ProjectId = @ProjectId ORDER BY CreatedAt DESC LIMIT 1";
        command.Parameters.AddWithValue("@ProjectId", projectId);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToStatistics(reader);
        }
        return null;
    }

    public async Task<ProjectSummaryStatistics> AddAsync(ProjectSummaryStatistics statistics)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProjectSummaryStatistics (ProjectId, TotalCoreLength, StatisticsJson, CreatedAt)
            VALUES (@ProjectId, @TotalCoreLength, @StatisticsJson, @CreatedAt);
            SELECT last_insert_rowid();";

        AddParameters(command, statistics);
        var result = await command.ExecuteScalarAsync();
        statistics.Id = Convert.ToInt32(result);
        return statistics;
    }

    public async Task UpdateAsync(ProjectSummaryStatistics statistics)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE ProjectSummaryStatistics SET
                TotalCoreLength = @TotalCoreLength,
                StatisticsJson = @StatisticsJson,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id";

        command.Parameters.AddWithValue("@Id", statistics.Id);
        AddParameters(command, statistics);
        command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProjectSummaryStatistics WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteByProjectIdAsync(int projectId)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProjectSummaryStatistics WHERE ProjectId = @ProjectId";
        command.Parameters.AddWithValue("@ProjectId", projectId);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(SqliteCommand command, ProjectSummaryStatistics statistics)
    {
        command.Parameters.AddWithValue("@ProjectId", statistics.ProjectId);
        command.Parameters.AddWithValue("@TotalCoreLength", statistics.TotalCoreLength);
        command.Parameters.AddWithValue("@StatisticsJson", statistics.StatisticsJson);
        command.Parameters.AddWithValue("@CreatedAt", statistics.CreatedAt.ToString("o"));
    }

    private static ProjectSummaryStatistics MapToStatistics(SqliteDataReader reader)
    {
        return new ProjectSummaryStatistics
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            ProjectId = reader.GetInt32(reader.GetOrdinal("ProjectId")),
            TotalCoreLength = reader.GetDouble(reader.GetOrdinal("TotalCoreLength")),
            StatisticsJson = reader.IsDBNull(reader.GetOrdinal("StatisticsJson")) ? string.Empty : reader.GetString(reader.GetOrdinal("StatisticsJson")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
        };
    }
}

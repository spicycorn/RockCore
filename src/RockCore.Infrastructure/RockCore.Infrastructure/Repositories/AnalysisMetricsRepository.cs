using Microsoft.Data.Sqlite;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Repositories;

public class AnalysisMetricsRepository : IAnalysisMetricsRepository
{
    private readonly RockCoreDbContext _context;

    public AnalysisMetricsRepository(RockCoreDbContext context)
    {
        _context = context;
    }

    public async Task<List<AnalysisMetrics>> GetByCorePhotoIdAsync(int corePhotoId)
    {
        var metrics = new List<AnalysisMetrics>();
        var connection = _context.GetConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CorePhotoId, MetricKey, MetricValue, MetricUnit, CreatedAt
            FROM AnalysisMetrics
            WHERE CorePhotoId = @corePhotoId";

        command.Parameters.AddWithValue("@corePhotoId", corePhotoId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            metrics.Add(new AnalysisMetrics
            {
                Id = reader.GetInt32(0),
                CorePhotoId = reader.GetInt32(1),
                MetricKey = reader.GetString(2),
                MetricValue = reader.GetString(3),
                MetricUnit = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5))
            });
        }

        return metrics;
    }

    public async Task AddAsync(AnalysisMetrics metric)
    {
        var connection = _context.GetConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO AnalysisMetrics (CorePhotoId, MetricKey, MetricValue, MetricUnit, CreatedAt)
            VALUES (@corePhotoId, @metricKey, @metricValue, @metricUnit, @createdAt)";

        command.Parameters.AddWithValue("@corePhotoId", metric.CorePhotoId);
        command.Parameters.AddWithValue("@metricKey", metric.MetricKey);
        command.Parameters.AddWithValue("@metricValue", metric.MetricValue);
        command.Parameters.AddWithValue("@metricUnit", metric.MetricUnit ?? string.Empty);
        command.Parameters.AddWithValue("@createdAt", metric.CreatedAt.ToString("o"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task AddRangeAsync(IEnumerable<AnalysisMetrics> metrics)
    {
        foreach (var metric in metrics)
        {
            await AddAsync(metric);
        }
    }

    public async Task DeleteByCorePhotoIdAsync(int corePhotoId)
    {
        var connection = _context.GetConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AnalysisMetrics WHERE CorePhotoId = @corePhotoId";
        command.Parameters.AddWithValue("@corePhotoId", corePhotoId);

        await command.ExecuteNonQueryAsync();
    }
}

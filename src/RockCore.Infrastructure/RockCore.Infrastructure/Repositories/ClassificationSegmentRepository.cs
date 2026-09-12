using Microsoft.Data.Sqlite;
using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Repositories;

public class ClassificationSegmentRepository : IClassificationSegmentRepository
{
    private readonly RockCoreDbContext _context;

    public ClassificationSegmentRepository(RockCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ClassificationSegment>> GetByBoreholeIdAsync(int boreholeId)
    {
        var segments = new List<ClassificationSegment>();
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ClassificationSegments WHERE BoreholeId = @BoreholeId ORDER BY DepthStart";
        command.Parameters.AddWithValue("@BoreholeId", boreholeId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            segments.Add(MapToClassificationSegment(reader));
        }
        return segments;
    }

    public async Task<IEnumerable<ClassificationSegment>> GetByProjectIdAsync(int projectId)
    {
        var segments = new List<ClassificationSegment>();
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT cs.* FROM ClassificationSegments cs
            INNER JOIN Boreholes b ON cs.BoreholeId = b.Id
            WHERE b.ProjectId = @ProjectId
            ORDER BY cs.BoreholeId, cs.DepthStart";
        command.Parameters.AddWithValue("@ProjectId", projectId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            segments.Add(MapToClassificationSegment(reader));
        }
        return segments;
    }

    public async Task<ClassificationSegment?> GetByIdAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ClassificationSegments WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToClassificationSegment(reader);
        }
        return null;
    }

    public async Task<ClassificationSegment> AddAsync(ClassificationSegment segment)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ClassificationSegments
                (BoreholeId, DepthStart, DepthEnd, RockClass, IntegrityLevel, RockType, RockStructureType, GroundwaterCondition, ConfidenceScore, Basis, CreatedAt)
            VALUES
                (@BoreholeId, @DepthStart, @DepthEnd, @RockClass, @IntegrityLevel, @RockType, @RockStructureType, @GroundwaterCondition, @ConfidenceScore, @Basis, @CreatedAt);
            SELECT last_insert_rowid();";

        AddParameters(command, segment);
        var result = await command.ExecuteScalarAsync();
        segment.Id = Convert.ToInt32(result);
        return segment;
    }

    public async Task AddRangeAsync(IEnumerable<ClassificationSegment> segments)
    {
        var connection = _context.GetConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var segment in segments)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO ClassificationSegments
                        (BoreholeId, DepthStart, DepthEnd, RockClass, IntegrityLevel, RockType, RockStructureType, GroundwaterCondition, ConfidenceScore, Basis, CreatedAt)
                    VALUES
                        (@BoreholeId, @DepthStart, @DepthEnd, @RockClass, @IntegrityLevel, @RockType, @RockStructureType, @GroundwaterCondition, @ConfidenceScore, @Basis, @CreatedAt);";

                AddParameters(command, segment);
                await command.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task UpdateAsync(ClassificationSegment segment)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE ClassificationSegments SET
                DepthStart = @DepthStart,
                DepthEnd = @DepthEnd,
                RockClass = @RockClass,
                IntegrityLevel = @IntegrityLevel,
                RockType = @RockType,
                RockStructureType = @RockStructureType,
                GroundwaterCondition = @GroundwaterCondition,
                ConfidenceScore = @ConfidenceScore,
                Basis = @Basis
            WHERE Id = @Id";

        command.Parameters.AddWithValue("@Id", segment.Id);
        AddParameters(command, segment);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateRangeAsync(IEnumerable<ClassificationSegment> segments)
    {
        foreach (var segment in segments)
        {
            await UpdateAsync(segment);
        }
    }

    public async Task DeleteAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ClassificationSegments WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteByBoreholeIdAsync(int boreholeId)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ClassificationSegments WHERE BoreholeId = @BoreholeId";
        command.Parameters.AddWithValue("@BoreholeId", boreholeId);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(SqliteCommand command, ClassificationSegment segment)
    {
        command.Parameters.AddWithValue("@BoreholeId", segment.BoreholeId);
        command.Parameters.AddWithValue("@DepthStart", segment.DepthStart);
        command.Parameters.AddWithValue("@DepthEnd", segment.DepthEnd);
        command.Parameters.AddWithValue("@RockClass", (int)segment.RockClass);
        command.Parameters.AddWithValue("@IntegrityLevel", (int)segment.IntegrityLevel);
        command.Parameters.AddWithValue("@RockType", (int)segment.RockType);
        command.Parameters.AddWithValue("@RockStructureType", (int)segment.RockStructureType);
        command.Parameters.AddWithValue("@GroundwaterCondition", (int)segment.GroundwaterCondition);
        command.Parameters.AddWithValue("@ConfidenceScore", segment.ConfidenceScore);
        command.Parameters.AddWithValue("@Basis", segment.Basis);
        command.Parameters.AddWithValue("@CreatedAt", segment.CreatedAt.ToString("o"));
    }

    private static ClassificationSegment MapToClassificationSegment(SqliteDataReader reader)
    {
        return new ClassificationSegment
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            BoreholeId = reader.GetInt32(reader.GetOrdinal("BoreholeId")),
            DepthStart = reader.GetDouble(reader.GetOrdinal("DepthStart")),
            DepthEnd = reader.GetDouble(reader.GetOrdinal("DepthEnd")),
            RockClass = (RockClass)reader.GetInt32(reader.GetOrdinal("RockClass")),
            IntegrityLevel = (IntegrityLevel)reader.GetInt32(reader.GetOrdinal("IntegrityLevel")),
            RockType = (RockType)reader.GetInt32(reader.GetOrdinal("RockType")),
            RockStructureType = (RockStructureType)reader.GetInt32(reader.GetOrdinal("RockStructureType")),
            GroundwaterCondition = (GroundwaterCondition)reader.GetInt32(reader.GetOrdinal("GroundwaterCondition")),
            ConfidenceScore = reader.GetDouble(reader.GetOrdinal("ConfidenceScore")),
            Basis = reader.IsDBNull(reader.GetOrdinal("Basis")) ? string.Empty : reader.GetString(reader.GetOrdinal("Basis")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")))
        };
    }
}

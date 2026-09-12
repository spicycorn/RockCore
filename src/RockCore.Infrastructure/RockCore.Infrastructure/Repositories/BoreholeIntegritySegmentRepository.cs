using Microsoft.Data.Sqlite;
using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Repositories;

public class BoreholeIntegritySegmentRepository : IBoreholeIntegritySegmentRepository
{
    private readonly RockCoreDbContext _context;

    public BoreholeIntegritySegmentRepository(RockCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<BoreholeIntegritySegment>> GetByBoreholeIdAsync(int boreholeId)
    {
        var segments = new List<BoreholeIntegritySegment>();
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM BoreholeIntegritySegments WHERE BoreholeId = @BoreholeId ORDER BY DepthStart";
        command.Parameters.AddWithValue("@BoreholeId", boreholeId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            segments.Add(MapToBoreholeIntegritySegment(reader));
        }
        return segments;
    }

    public async Task<BoreholeIntegritySegment?> GetByIdAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM BoreholeIntegritySegments WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToBoreholeIntegritySegment(reader);
        }
        return null;
    }

    public async Task<BoreholeIntegritySegment> AddAsync(BoreholeIntegritySegment segment)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO BoreholeIntegritySegments
                (BoreholeId, DepthStart, DepthEnd, IntegrityLevel, RockType, RockStructureType,
                 RockHardnessLevel, RockHomogeneity, GroundwaterCondition, CaveAxisAngleLessThan30, CreatedAt, UpdatedAt)
            VALUES
                (@BoreholeId, @DepthStart, @DepthEnd, @IntegrityLevel, @RockType, @RockStructureType,
                 @RockHardnessLevel, @RockHomogeneity, @GroundwaterCondition, @CaveAxisAngleLessThan30, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();";

        AddParameters(command, segment);
        var result = await command.ExecuteScalarAsync();
        segment.Id = Convert.ToInt32(result);
        return segment;
    }

    public async Task AddRangeAsync(IEnumerable<BoreholeIntegritySegment> segments)
    {
        var connection = _context.GetConnection();
        foreach (var segment in segments)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO BoreholeIntegritySegments
                    (BoreholeId, DepthStart, DepthEnd, IntegrityLevel, RockType, RockStructureType,
                     RockHardnessLevel, RockHomogeneity, GroundwaterCondition, CaveAxisAngleLessThan30, CreatedAt, UpdatedAt)
                VALUES
                    (@BoreholeId, @DepthStart, @DepthEnd, @IntegrityLevel, @RockType, @RockStructureType,
                     @RockHardnessLevel, @RockHomogeneity, @GroundwaterCondition, @CaveAxisAngleLessThan30, @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();";
            AddParameters(command, segment);
            var result = await command.ExecuteScalarAsync();
            segment.Id = Convert.ToInt32(result);
        }
    }

    public async Task UpdateAsync(BoreholeIntegritySegment segment)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE BoreholeIntegritySegments SET
                DepthStart = @DepthStart,
                DepthEnd = @DepthEnd,
                IntegrityLevel = @IntegrityLevel,
                RockType = @RockType,
                RockStructureType = @RockStructureType,
                RockHardnessLevel = @RockHardnessLevel,
                RockHomogeneity = @RockHomogeneity,
                GroundwaterCondition = @GroundwaterCondition,
                CaveAxisAngleLessThan30 = @CaveAxisAngleLessThan30,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id";

        command.Parameters.AddWithValue("@Id", segment.Id);
        AddParameters(command, segment);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateRangeAsync(IEnumerable<BoreholeIntegritySegment> segments)
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
        command.CommandText = "DELETE FROM BoreholeIntegritySegments WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteByBoreholeIdAsync(int boreholeId)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM BoreholeIntegritySegments WHERE BoreholeId = @BoreholeId";
        command.Parameters.AddWithValue("@BoreholeId", boreholeId);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(SqliteCommand command, BoreholeIntegritySegment segment)
    {
        command.Parameters.AddWithValue("@BoreholeId", segment.BoreholeId);
        command.Parameters.AddWithValue("@DepthStart", segment.DepthStart);
        command.Parameters.AddWithValue("@DepthEnd", segment.DepthEnd);
        command.Parameters.AddWithValue("@IntegrityLevel", (int)segment.IntegrityLevel);
        command.Parameters.AddWithValue("@RockType", (int)segment.RockType);
        command.Parameters.AddWithValue("@RockStructureType", (int)segment.RockStructureType);
        command.Parameters.AddWithValue("@RockHardnessLevel", (int)segment.RockHardnessLevel);
        command.Parameters.AddWithValue("@RockHomogeneity", (int)segment.RockHomogeneity);
        command.Parameters.AddWithValue("@GroundwaterCondition", (int)segment.GroundwaterCondition);
        command.Parameters.AddWithValue("@CaveAxisAngleLessThan30",
            segment.CaveAxisAngleLessThan30.HasValue ? (segment.CaveAxisAngleLessThan30.Value ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("@CreatedAt", segment.CreatedAt.ToString("o"));
        command.Parameters.AddWithValue("@UpdatedAt", segment.UpdatedAt.ToString("o"));
    }

    private static BoreholeIntegritySegment MapToBoreholeIntegritySegment(SqliteDataReader reader)
    {
        var ordinalCaveAxis = reader.GetOrdinal("CaveAxisAngleLessThan30");
        int? caveAxisValue = reader.IsDBNull(ordinalCaveAxis) ? null : reader.GetInt32(ordinalCaveAxis);

        return new BoreholeIntegritySegment
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            BoreholeId = reader.GetInt32(reader.GetOrdinal("BoreholeId")),
            DepthStart = reader.GetDouble(reader.GetOrdinal("DepthStart")),
            DepthEnd = reader.GetDouble(reader.GetOrdinal("DepthEnd")),
            IntegrityLevel = (IntegrityLevel)reader.GetInt32(reader.GetOrdinal("IntegrityLevel")),
            RockType = (RockType)reader.GetInt32(reader.GetOrdinal("RockType")),
            RockStructureType = (RockStructureType)reader.GetInt32(reader.GetOrdinal("RockStructureType")),
            RockHardnessLevel = (RockHardnessLevel)reader.GetInt32(reader.GetOrdinal("RockHardnessLevel")),
            RockHomogeneity = (RockHomogeneity)reader.GetInt32(reader.GetOrdinal("RockHomogeneity")),
            GroundwaterCondition = (GroundwaterCondition)reader.GetInt32(reader.GetOrdinal("GroundwaterCondition")),
            CaveAxisAngleLessThan30 = caveAxisValue.HasValue ? caveAxisValue.Value == 1 : null,
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt"))
                ? DateTime.Now
                : DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
        };
    }
}

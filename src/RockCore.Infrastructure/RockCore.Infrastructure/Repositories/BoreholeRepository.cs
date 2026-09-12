using Microsoft.Data.Sqlite;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Repositories;

public class BoreholeRepository : IBoreholeRepository
{
    private readonly RockCoreDbContext _context;

    public BoreholeRepository(RockCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Borehole>> GetByProjectIdAsync(int projectId)
    {
        var boreholes = new List<Borehole>();
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Boreholes WHERE ProjectId = @ProjectId ORDER BY Number";
        command.Parameters.AddWithValue("@ProjectId", projectId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            boreholes.Add(MapToBorehole(reader));
        }
        return boreholes;
    }

    public async Task<Borehole?> GetByIdAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Boreholes WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToBorehole(reader);
        }
        return null;
    }

    public async Task<Borehole> AddAsync(Borehole borehole)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Boreholes (ProjectId, Number, OrificeElevation, TotalDepth, GroundwaterDepth,
                Azimuth, InclinationAngle, OrificeX, OrificeY, OrificeZ, CreatedAt)
            VALUES (@ProjectId, @Number, @OrificeElevation, @TotalDepth, @GroundwaterDepth,
                @Azimuth, @InclinationAngle, @OrificeX, @OrificeY, @OrificeZ, @CreatedAt);
            SELECT last_insert_rowid();";

        command.Parameters.AddWithValue("@ProjectId", borehole.ProjectId);
        command.Parameters.AddWithValue("@Number", borehole.Number);
        command.Parameters.AddWithValue("@OrificeElevation", borehole.OrificeElevation);
        command.Parameters.AddWithValue("@TotalDepth", borehole.TotalDepth);
        command.Parameters.AddWithValue("@GroundwaterDepth", borehole.GroundwaterDepth);
        command.Parameters.AddWithValue("@Azimuth", borehole.Azimuth);
        command.Parameters.AddWithValue("@InclinationAngle", borehole.InclinationAngle);
        command.Parameters.AddWithValue("@OrificeX", borehole.OrificeX);
        command.Parameters.AddWithValue("@OrificeY", borehole.OrificeY);
        command.Parameters.AddWithValue("@OrificeZ", borehole.OrificeZ);
        command.Parameters.AddWithValue("@CreatedAt", borehole.CreatedAt.ToString("o"));

        var result = await command.ExecuteScalarAsync();
        borehole.Id = Convert.ToInt32(result);
        return borehole;
    }

    public async Task UpdateAsync(Borehole borehole)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Boreholes SET
                Number = @Number,
                OrificeElevation = @OrificeElevation,
                TotalDepth = @TotalDepth,
                GroundwaterDepth = @GroundwaterDepth,
                Azimuth = @Azimuth,
                InclinationAngle = @InclinationAngle,
                OrificeX = @OrificeX,
                OrificeY = @OrificeY,
                OrificeZ = @OrificeZ,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id";

        command.Parameters.AddWithValue("@Id", borehole.Id);
        command.Parameters.AddWithValue("@Number", borehole.Number);
        command.Parameters.AddWithValue("@OrificeElevation", borehole.OrificeElevation);
        command.Parameters.AddWithValue("@TotalDepth", borehole.TotalDepth);
        command.Parameters.AddWithValue("@GroundwaterDepth", borehole.GroundwaterDepth);
        command.Parameters.AddWithValue("@Azimuth", borehole.Azimuth);
        command.Parameters.AddWithValue("@InclinationAngle", borehole.InclinationAngle);
        command.Parameters.AddWithValue("@OrificeX", borehole.OrificeX);
        command.Parameters.AddWithValue("@OrificeY", borehole.OrificeY);
        command.Parameters.AddWithValue("@OrificeZ", borehole.OrificeZ);
        command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Boreholes WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static Borehole MapToBorehole(SqliteDataReader reader)
    {
        return new Borehole
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            ProjectId = reader.GetInt32(reader.GetOrdinal("ProjectId")),
            Number = reader.GetString(reader.GetOrdinal("Number")),
            OrificeElevation = reader.GetDouble(reader.GetOrdinal("OrificeElevation")),
            TotalDepth = reader.GetDouble(reader.GetOrdinal("TotalDepth")),
            GroundwaterDepth = reader.GetDouble(reader.GetOrdinal("GroundwaterDepth")),
            Azimuth = reader.GetDouble(reader.GetOrdinal("Azimuth")),
            InclinationAngle = reader.GetDouble(reader.GetOrdinal("InclinationAngle")),
            OrificeX = reader.GetDouble(reader.GetOrdinal("OrificeX")),
            OrificeY = reader.GetDouble(reader.GetOrdinal("OrificeY")),
            OrificeZ = reader.GetDouble(reader.GetOrdinal("OrificeZ")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
        };
    }
}

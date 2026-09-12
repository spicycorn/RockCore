using Microsoft.Data.Sqlite;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Repositories;

public class StructuralPlaneRepository : IStructuralPlaneRepository
{
    private readonly RockCoreDbContext _context;

    public StructuralPlaneRepository(RockCoreDbContext context)
    {
        _context = context;
    }

    public async Task<List<StructuralPlane>> GetByCorePhotoIdAsync(int corePhotoId)
    {
        var planes = new List<StructuralPlane>();
        var connection = _context.GetConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CorePhotoId, PlaneType, Strike, Dip, DepthInPhoto, GlobalDepth,
                   ApertureWidthCm, Roughness, ImageX, ImageY, IsUserModified, CreatedAt
            FROM StructuralPlanes
            WHERE CorePhotoId = @corePhotoId
            ORDER BY DepthInPhoto";

        command.Parameters.AddWithValue("@corePhotoId", corePhotoId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            planes.Add(new StructuralPlane
            {
                Id = reader.GetInt32(0),
                CorePhotoId = reader.GetInt32(1),
                PlaneType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Strike = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                Dip = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                DepthInPhoto = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                GlobalDepth = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                ApertureWidthCm = reader.GetDouble(7),
                Roughness = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                ImageX = reader.GetDouble(9),
                ImageY = reader.GetDouble(10),
                IsUserModified = reader.GetInt32(11) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(12))
            });
        }

        return planes;
    }

    public async Task<int> AddAsync(StructuralPlane plane)
    {
        var connection = _context.GetConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO StructuralPlanes (CorePhotoId, PlaneType, Strike, Dip, DepthInPhoto, GlobalDepth,
                                          ApertureWidthCm, Roughness, ImageX, ImageY, IsUserModified, CreatedAt)
            VALUES (@corePhotoId, @planeType, @strike, @dip, @depthInPhoto, @globalDepth,
                    @apertureWidthCm, @roughness, @imageX, @imageY, @isUserModified, @createdAt);
            SELECT last_insert_rowid();";

        command.Parameters.AddWithValue("@corePhotoId", plane.CorePhotoId);
        command.Parameters.AddWithValue("@planeType", plane.PlaneType ?? string.Empty);
        command.Parameters.AddWithValue("@strike", plane.Strike.HasValue ? plane.Strike.Value : DBNull.Value);
        command.Parameters.AddWithValue("@dip", plane.Dip.HasValue ? plane.Dip.Value : DBNull.Value);
        command.Parameters.AddWithValue("@depthInPhoto", plane.DepthInPhoto.HasValue ? plane.DepthInPhoto.Value : DBNull.Value);
        command.Parameters.AddWithValue("@globalDepth", plane.GlobalDepth.HasValue ? plane.GlobalDepth.Value : DBNull.Value);
        command.Parameters.AddWithValue("@apertureWidthCm", plane.ApertureWidthCm);
        command.Parameters.AddWithValue("@roughness", plane.Roughness ?? string.Empty);
        command.Parameters.AddWithValue("@imageX", plane.ImageX);
        command.Parameters.AddWithValue("@imageY", plane.ImageY);
        command.Parameters.AddWithValue("@isUserModified", plane.IsUserModified ? 1 : 0);
        command.Parameters.AddWithValue("@createdAt", plane.CreatedAt.ToString("o"));

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task AddRangeAsync(IEnumerable<StructuralPlane> planes)
    {
        foreach (var plane in planes)
        {
            await AddAsync(plane);
        }
    }

    public async Task UpdateAsync(StructuralPlane plane)
    {
        var connection = _context.GetConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE StructuralPlanes
            SET PlaneType = @planeType, Strike = @strike, Dip = @dip,
                DepthInPhoto = @depthInPhoto, GlobalDepth = @globalDepth,
                ApertureWidthCm = @apertureWidthCm, Roughness = @roughness,
                ImageX = @imageX, ImageY = @imageY, IsUserModified = @isUserModified
            WHERE Id = @id";

        command.Parameters.AddWithValue("@id", plane.Id);
        command.Parameters.AddWithValue("@planeType", plane.PlaneType ?? string.Empty);
        command.Parameters.AddWithValue("@strike", plane.Strike.HasValue ? plane.Strike.Value : DBNull.Value);
        command.Parameters.AddWithValue("@dip", plane.Dip.HasValue ? plane.Dip.Value : DBNull.Value);
        command.Parameters.AddWithValue("@depthInPhoto", plane.DepthInPhoto.HasValue ? plane.DepthInPhoto.Value : DBNull.Value);
        command.Parameters.AddWithValue("@globalDepth", plane.GlobalDepth.HasValue ? plane.GlobalDepth.Value : DBNull.Value);
        command.Parameters.AddWithValue("@apertureWidthCm", plane.ApertureWidthCm);
        command.Parameters.AddWithValue("@roughness", plane.Roughness ?? string.Empty);
        command.Parameters.AddWithValue("@imageX", plane.ImageX);
        command.Parameters.AddWithValue("@imageY", plane.ImageY);
        command.Parameters.AddWithValue("@isUserModified", plane.IsUserModified ? 1 : 0);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var connection = _context.GetConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StructuralPlanes WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteByCorePhotoIdAsync(int corePhotoId)
    {
        var connection = _context.GetConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StructuralPlanes WHERE CorePhotoId = @corePhotoId";
        command.Parameters.AddWithValue("@corePhotoId", corePhotoId);

        await command.ExecuteNonQueryAsync();
    }
}

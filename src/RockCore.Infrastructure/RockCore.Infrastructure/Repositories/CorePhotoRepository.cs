using Microsoft.Data.Sqlite;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Core.Enums;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Repositories;

public class CorePhotoRepository : ICorePhotoRepository
{
    private readonly RockCoreDbContext _context;

    public CorePhotoRepository(RockCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<CorePhoto>> GetByBoreholeIdAsync(int boreholeId)
    {
        var photos = new List<CorePhoto>();
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CorePhotos WHERE BoreholeId = @BoreholeId ORDER BY DepthStart";
        command.Parameters.AddWithValue("@BoreholeId", boreholeId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            photos.Add(MapToCorePhoto(reader));
        }
        return photos;
    }

    public async Task<CorePhoto?> GetByIdAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CorePhotos WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToCorePhoto(reader);
        }
        return null;
    }

    public async Task<CorePhoto> AddAsync(CorePhoto corePhoto)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO CorePhotos (BoreholeId, FileName, RelativePath, DepthStart, DepthEnd, BoxNumber,
                IntegrityLevel, IntegrityIndex, RQD, JointCount, AvgJointSpacingCm, IsFragmented,
                AnalysisStatus, IsUserModified, AnalysisResultJson, CreatedAt)
            VALUES (@BoreholeId, @FileName, @RelativePath, @DepthStart, @DepthEnd, @BoxNumber,
                @IntegrityLevel, @IntegrityIndex, @RQD, @JointCount, @AvgJointSpacingCm, @IsFragmented,
                @AnalysisStatus, @IsUserModified, @AnalysisResultJson, @CreatedAt);
            SELECT last_insert_rowid();";

        AddCorePhotoParameters(command, corePhoto);

        var result = await command.ExecuteScalarAsync();
        corePhoto.Id = Convert.ToInt32(result);
        return corePhoto;
    }

    public async Task AddRangeAsync(IEnumerable<CorePhoto> corePhotos)
    {
        var connection = _context.GetConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var photo in corePhotos)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO CorePhotos (BoreholeId, FileName, RelativePath, DepthStart, DepthEnd, BoxNumber,
                        IntegrityLevel, IntegrityIndex, RQD, JointCount, AvgJointSpacingCm, IsFragmented,
                        AnalysisStatus, IsUserModified, AnalysisResultJson, CreatedAt)
                    VALUES (@BoreholeId, @FileName, @RelativePath, @DepthStart, @DepthEnd, @BoxNumber,
                        @IntegrityLevel, @IntegrityIndex, @RQD, @JointCount, @AvgJointSpacingCm, @IsFragmented,
                        @AnalysisStatus, @IsUserModified, @AnalysisResultJson, @CreatedAt);";

                AddCorePhotoParameters(command, photo);
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

    public async Task UpdateAsync(CorePhoto corePhoto)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE CorePhotos SET
                FileName = @FileName,
                RelativePath = @RelativePath,
                DepthStart = @DepthStart,
                DepthEnd = @DepthEnd,
                BoxNumber = @BoxNumber,
                IntegrityLevel = @IntegrityLevel,
                IntegrityIndex = @IntegrityIndex,
                RQD = @RQD,
                JointCount = @JointCount,
                AvgJointSpacingCm = @AvgJointSpacingCm,
                IsFragmented = @IsFragmented,
                AnalysisStatus = @AnalysisStatus,
                IsUserModified = @IsUserModified,
                AnalysisResultJson = @AnalysisResultJson
            WHERE Id = @Id";

        command.Parameters.AddWithValue("@Id", corePhoto.Id);
        AddCorePhotoParameters(command, corePhoto);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CorePhotos WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteByBoreholeIdAsync(int boreholeId)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CorePhotos WHERE BoreholeId = @BoreholeId";
        command.Parameters.AddWithValue("@BoreholeId", boreholeId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<double?> GetMaxDepthEndAsync(int boreholeId)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(DepthEnd) FROM CorePhotos WHERE BoreholeId = @BoreholeId";
        command.Parameters.AddWithValue("@BoreholeId", boreholeId);

        var result = await command.ExecuteScalarAsync();
        if (result == null || Convert.IsDBNull(result)) return null;
        return Convert.ToDouble(result);
    }

    public async Task UpdateRangeAsync(IEnumerable<CorePhoto> corePhotos)
    {
        var connection = _context.GetConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var photo in corePhotos)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    UPDATE CorePhotos SET
                        FileName = @FileName,
                        RelativePath = @RelativePath,
                        DepthStart = @DepthStart,
                        DepthEnd = @DepthEnd,
                        BoxNumber = @BoxNumber,
                        IntegrityLevel = @IntegrityLevel,
                        IntegrityIndex = @IntegrityIndex,
                        RQD = @RQD,
                        JointCount = @JointCount,
                        AvgJointSpacingCm = @AvgJointSpacingCm,
                        IsFragmented = @IsFragmented,
                        AnalysisStatus = @AnalysisStatus,
                        IsUserModified = @IsUserModified,
                        AnalysisResultJson = @AnalysisResultJson
                    WHERE Id = @Id";
                command.Parameters.AddWithValue("@Id", photo.Id);
                AddCorePhotoParameters(command, photo);
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

    private static void AddCorePhotoParameters(SqliteCommand command, CorePhoto photo)
    {
        command.Parameters.AddWithValue("@BoreholeId", photo.BoreholeId);
        command.Parameters.AddWithValue("@FileName", photo.FileName);
        command.Parameters.AddWithValue("@RelativePath", photo.RelativePath ?? string.Empty);
        command.Parameters.AddWithValue("@DepthStart", photo.DepthStart);
        command.Parameters.AddWithValue("@DepthEnd", photo.DepthEnd);
        command.Parameters.AddWithValue("@BoxNumber", photo.BoxNumber);
        command.Parameters.AddWithValue("@IntegrityLevel", (int)photo.IntegrityLevel);
        command.Parameters.AddWithValue("@IntegrityIndex", photo.IntegrityIndex);
        command.Parameters.AddWithValue("@RQD", photo.RQD);
        command.Parameters.AddWithValue("@JointCount", photo.JointCount);
        command.Parameters.AddWithValue("@AvgJointSpacingCm", photo.AvgJointSpacingCm);
        command.Parameters.AddWithValue("@IsFragmented", photo.IsFragmented ? 1 : 0);
        command.Parameters.AddWithValue("@AnalysisStatus", photo.AnalysisStatus);
        command.Parameters.AddWithValue("@IsUserModified", photo.IsUserModified ? 1 : 0);
        command.Parameters.AddWithValue("@AnalysisResultJson", photo.AnalysisResultJson ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@CreatedAt", photo.CreatedAt.ToString("o"));
    }

    private static CorePhoto MapToCorePhoto(SqliteDataReader reader)
    {
        return new CorePhoto
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            BoreholeId = reader.GetInt32(reader.GetOrdinal("BoreholeId")),
            FileName = reader.GetString(reader.GetOrdinal("FileName")),
            RelativePath = reader.IsDBNull(reader.GetOrdinal("RelativePath")) ? string.Empty : reader.GetString(reader.GetOrdinal("RelativePath")),
            DepthStart = reader.GetDouble(reader.GetOrdinal("DepthStart")),
            DepthEnd = reader.GetDouble(reader.GetOrdinal("DepthEnd")),
            BoxNumber = reader.GetInt32(reader.GetOrdinal("BoxNumber")),
            IntegrityLevel = (IntegrityLevel)reader.GetInt32(reader.GetOrdinal("IntegrityLevel")),
            IntegrityIndex = reader.GetDouble(reader.GetOrdinal("IntegrityIndex")),
            RQD = reader.GetDouble(reader.GetOrdinal("RQD")),
            JointCount = reader.GetInt32(reader.GetOrdinal("JointCount")),
            AvgJointSpacingCm = reader.GetDouble(reader.GetOrdinal("AvgJointSpacingCm")),
            IsFragmented = reader.GetInt32(reader.GetOrdinal("IsFragmented")) == 1,
            AnalysisStatus = reader.GetString(reader.GetOrdinal("AnalysisStatus")),
            IsUserModified = reader.GetInt32(reader.GetOrdinal("IsUserModified")) == 1,
            AnalysisResultJson = reader.IsDBNull(reader.GetOrdinal("AnalysisResultJson")) ? null : reader.GetString(reader.GetOrdinal("AnalysisResultJson")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")))
        };
    }
}

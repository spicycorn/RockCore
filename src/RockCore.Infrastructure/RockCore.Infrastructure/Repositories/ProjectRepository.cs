using Microsoft.Data.Sqlite;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Infrastructure.Data;

namespace RockCore.Infrastructure.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly RockCoreDbContext _context;

    public ProjectRepository(RockCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Project>> GetAllAsync()
    {
        var projects = new List<Project>();
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Projects ORDER BY CreatedAt DESC";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            projects.Add(MapToProject(reader));
        }
        return projects;
    }

    public async Task<Project?> GetByIdAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Projects WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToProject(reader);
        }
        return null;
    }

    public async Task<Project> AddAsync(Project project)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Projects (ProjectNumber, Name, Phase, CaveAxisAzimuth, CreatedAt)
            VALUES (@ProjectNumber, @Name, @Phase, @CaveAxisAzimuth, @CreatedAt);
            SELECT last_insert_rowid();";

        command.Parameters.AddWithValue("@ProjectNumber", project.ProjectNumber ?? string.Empty);
        command.Parameters.AddWithValue("@Name", project.Name);
        command.Parameters.AddWithValue("@Phase", project.Phase ?? string.Empty);
        command.Parameters.AddWithValue("@CaveAxisAzimuth", project.CaveAxisAzimuth);
        command.Parameters.AddWithValue("@CreatedAt", project.CreatedAt.ToString("o"));

        var result = await command.ExecuteScalarAsync();
        project.Id = Convert.ToInt32(result);
        return project;
    }

    public async Task UpdateAsync(Project project)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Projects SET
                ProjectNumber = @ProjectNumber,
                Name = @Name,
                Phase = @Phase,
                CaveAxisAzimuth = @CaveAxisAzimuth,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id";

        command.Parameters.AddWithValue("@Id", project.Id);
        command.Parameters.AddWithValue("@ProjectNumber", project.ProjectNumber ?? string.Empty);
        command.Parameters.AddWithValue("@Name", project.Name);
        command.Parameters.AddWithValue("@Phase", project.Phase ?? string.Empty);
        command.Parameters.AddWithValue("@CaveAxisAzimuth", project.CaveAxisAzimuth);
        command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var connection = _context.GetConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Projects WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static Project MapToProject(SqliteDataReader reader)
    {
        string projectNumber = string.Empty;
        try
        {
            var projectNumberIndex = reader.GetOrdinal("ProjectNumber");
            if (!reader.IsDBNull(projectNumberIndex))
                projectNumber = reader.GetString(projectNumberIndex);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 旧数据库没有 ProjectNumber 列，忽略。
        }

        return new Project
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            ProjectNumber = projectNumber,
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Phase = reader.IsDBNull(reader.GetOrdinal("Phase")) ? string.Empty : reader.GetString(reader.GetOrdinal("Phase")),
            CaveAxisAzimuth = reader.GetDouble(reader.GetOrdinal("CaveAxisAzimuth")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
        };
    }
}

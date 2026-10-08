using Microsoft.Data.Sqlite;
using RockCore.Core.Models;
using RockCore.Core.Enums;

namespace RockCore.Infrastructure.Data;

public class RockCoreDbContext : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public RockCoreDbContext(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
        InitializeDatabase();
    }

    public SqliteConnection GetConnection()
    {
        if (_connection == null)
        {
            _connection = new SqliteConnection(_connectionString);
        }
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
            // SQLite 默认不启用外键约束，必须对每个连接显式开启，
            // 否则 schema 中的 ON DELETE CASCADE 不会生效（删除父记录会留下孤儿子记录）。
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys = ON;";
                cmd.ExecuteNonQuery();
            }
        }
        return _connection;
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var createTablesCommand = connection.CreateCommand();
            createTablesCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS Projects (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectNumber TEXT,
                Name TEXT NOT NULL,
                Phase TEXT,
                CaveAxisAzimuth REAL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT
            );

            CREATE TABLE IF NOT EXISTS Boreholes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                Number TEXT NOT NULL,
                OrificeElevation REAL DEFAULT 0,
                TotalDepth REAL DEFAULT 0,
                GroundwaterDepth REAL DEFAULT 0,
                Azimuth REAL DEFAULT 0,
                InclinationAngle REAL DEFAULT 0,
                OrificeX REAL DEFAULT 0,
                OrificeY REAL DEFAULT 0,
                OrificeZ REAL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CorePhotos (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BoreholeId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                RelativePath TEXT,
                DepthStart REAL NOT NULL,
                DepthEnd REAL NOT NULL,
                BoxNumber INTEGER DEFAULT 0,
                IntegrityLevel INTEGER DEFAULT 0,
                IntegrityIndex REAL DEFAULT 0,
                RQD REAL DEFAULT 0,
                JointCount INTEGER DEFAULT 0,
                AvgJointSpacingCm REAL DEFAULT 0,
                IsFragmented INTEGER DEFAULT 0,
                AnalysisStatus TEXT DEFAULT '未分析',
                IsUserModified INTEGER DEFAULT 0,
                AnalysisResultJson TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (BoreholeId) REFERENCES Boreholes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS StructuralPlanes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CorePhotoId INTEGER NOT NULL,
                PlaneType TEXT,
                Strike REAL DEFAULT 0,
                Dip REAL DEFAULT 0,
                DepthInPhoto REAL DEFAULT 0,
                GlobalDepth REAL DEFAULT 0,
                ApertureWidthCm REAL DEFAULT 0,
                Roughness TEXT,
                ImageX REAL DEFAULT 0,
                ImageY REAL DEFAULT 0,
                IsUserModified INTEGER DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CorePhotoId) REFERENCES CorePhotos(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ClassificationSegments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BoreholeId INTEGER NOT NULL,
                DepthStart REAL NOT NULL,
                DepthEnd REAL NOT NULL,
                RockClass INTEGER NOT NULL,
                IntegrityLevel INTEGER NOT NULL,
                RockType INTEGER NOT NULL,
                RockStructureType INTEGER NOT NULL,
                GroundwaterCondition INTEGER NOT NULL,
                ConfidenceScore REAL DEFAULT 0,
                Basis TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (BoreholeId) REFERENCES Boreholes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS BoreholeIntegritySegments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BoreholeId INTEGER NOT NULL,
                DepthStart REAL NOT NULL,
                DepthEnd REAL NOT NULL,
                IntegrityLevel INTEGER NOT NULL,
                RockType INTEGER NOT NULL,
                RockStructureType INTEGER NOT NULL,
                RockHardnessLevel INTEGER NOT NULL,
                RockHomogeneity INTEGER NOT NULL,
                GroundwaterCondition INTEGER NOT NULL,
                CaveAxisAngleLessThan30 INTEGER,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (BoreholeId) REFERENCES Boreholes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AnalysisMetrics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CorePhotoId INTEGER NOT NULL,
                MetricKey TEXT NOT NULL,
                MetricValue TEXT NOT NULL,
                MetricUnit TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CorePhotoId) REFERENCES CorePhotos(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ProjectSummaryStatistics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                TotalCoreLength REAL DEFAULT 0,
                StatisticsJson TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ImportBatches (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BoreholeId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                SourceType TEXT NOT NULL DEFAULT 'Excel',
                RowCount INTEGER NOT NULL DEFAULT 0,
                Note TEXT,
                ImportedAt TEXT NOT NULL,
                FOREIGN KEY (BoreholeId) REFERENCES Boreholes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS RunStatistics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BoreholeId INTEGER NOT NULL,
                ImportBatchId INTEGER NOT NULL,
                RowNumber INTEGER NOT NULL DEFAULT 0,
                RunNo INTEGER,
                RunLengthM REAL NOT NULL,
                FragCount INTEGER NOT NULL DEFAULT 0,
                PieceCount INTEGER NOT NULL DEFAULT 0,
                TotalLengthCm REAL NOT NULL DEFAULT 0,
                Rqd REAL,
                DepthStart REAL NOT NULL,
                DepthEnd REAL NOT NULL,
                JointCount INTEGER NOT NULL DEFAULT 0,
                AvgSpacingCm REAL NOT NULL DEFAULT 0,
                IntegrityLevel INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (BoreholeId) REFERENCES Boreholes(Id) ON DELETE CASCADE,
                FOREIGN KEY (ImportBatchId) REFERENCES ImportBatches(Id) ON DELETE CASCADE
            );
        ";
        createTablesCommand.ExecuteNonQuery();

        // 迁移：对旧库补齐列
        MigrateAddColumn(connection, "Projects", "ProjectNumber", "TEXT");
        MigrateAddColumn(connection, "BoreholeIntegritySegments", "ImportBatchId", "INTEGER");
    }

    private static void MigrateAddColumn(Microsoft.Data.Sqlite.SqliteConnection connection,
        string table, string column, string type)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @col;";
        check.Parameters.AddWithValue("@col", column);
        if (Convert.ToInt32(check.ExecuteScalar()) == 0)
        {
            using var add = connection.CreateCommand();
            add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
            add.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}

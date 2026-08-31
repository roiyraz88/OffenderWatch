using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>
/// Shared throwaway-SQLite-file pattern (see EnvironmentServiceTests) so
/// every test class gets its own isolated database — never
/// test-management/data/testmanagement.db, and never any real OffenderWatch
/// application data.
/// </summary>
public abstract class TestDatabaseFixture : IDisposable
{
    private readonly string _dbPath;
    protected readonly TestManagementDbContext Db;

    protected TestDatabaseFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tm-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TestManagementDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        Db = new TestManagementDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        var connection = Db.Database.GetDbConnection();
        Db.Dispose();
        if (connection is SqliteConnection sqliteConnection)
        {
            SqliteConnection.ClearPool(sqliteConnection);
        }
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}

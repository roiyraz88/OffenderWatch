using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.DTOs;
using OffenderWatch.TestManagement.Server.Services;
using Xunit;

namespace OffenderWatch.TestManagement.Server.Tests;

/// <summary>
/// Step 3.13 — focused tests of EnvironmentService's own rules (TM-01).
/// These exercise the Part 5 platform itself, not the Part 3 OffenderWatch
/// automation, and never touch the real (or any shared) OffenderWatch app.
/// Each test gets its own throwaway SQLite file under the OS temp
/// directory, created fresh and deleted on dispose — never
/// test-management/data/testmanagement.db.
/// </summary>
public class EnvironmentServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestManagementDbContext _db;
    private readonly EnvironmentService _sut;

    public EnvironmentServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tm-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TestManagementDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new TestManagementDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new EnvironmentService(_db);
    }

    public void Dispose()
    {
        var connection = _db.Database.GetDbConnection();
        _db.Dispose();
        // EF's SQLite provider pools connections by connection string, which
        // keeps the file locked after Dispose() unless the pool is cleared.
        if (connection is SqliteConnection sqliteConnection)
        {
            SqliteConnection.ClearPool(sqliteConnection);
        }
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static CreateEnvironmentRequest ValidCreateRequest(string name, bool isDefault = false) => new()
    {
        Name = name,
        BaseUrl = $"https://example.com/{name}",
        IsDefault = isDefault,
    };

    [Fact]
    public async Task CreateAsync_FirstEnvironment_BecomesDefaultAutomatically()
    {
        var created = await _sut.CreateAsync(ValidCreateRequest("Dev", isDefault: false));

        Assert.True(created.IsDefault);
    }

    [Fact]
    public async Task SetDefaultAsync_OnlyOneDefaultExists_AfterChangingDefault()
    {
        var first = await _sut.CreateAsync(ValidCreateRequest("Dev"));
        var second = await _sut.CreateAsync(ValidCreateRequest("Staging"));

        await _sut.SetDefaultAsync(second.Id);

        var all = await _sut.GetAllAsync();
        var defaults = all.Where(e => e.IsDefault).ToList();

        Assert.Single(defaults);
        Assert.Equal(second.Id, defaults[0].Id);
        Assert.NotEqual(first.Id, defaults[0].Id);
    }

    [Fact]
    public async Task CreateAsync_SecondEnvironmentDoesNotRequestDefault_FirstRemainsDefault()
    {
        var first = await _sut.CreateAsync(ValidCreateRequest("Dev"));
        await _sut.CreateAsync(ValidCreateRequest("Staging", isDefault: false));

        var refreshedFirst = await _sut.GetByIdAsync(first.Id);
        Assert.True(refreshedFirst.IsDefault);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_IsRejected()
    {
        await _sut.CreateAsync(ValidCreateRequest("Staging"));

        await Assert.ThrowsAsync<EnvironmentConflictException>(
            () => _sut.CreateAsync(ValidCreateRequest("staging"))); // case-insensitive duplicate
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("/relative/path")]
    public async Task CreateAsync_InvalidBaseUrl_IsRejected(string invalidBaseUrl)
    {
        var request = new CreateEnvironmentRequest { Name = "Dev", BaseUrl = invalidBaseUrl };

        await Assert.ThrowsAsync<EnvironmentValidationException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_EmptyName_IsRejected()
    {
        var request = new CreateEnvironmentRequest { Name = "   ", BaseUrl = "https://example.com" };

        await Assert.ThrowsAsync<EnvironmentValidationException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task DeleteAsync_DeletingDefault_SelectsAnotherDefaultWhenPossible()
    {
        var first = await _sut.CreateAsync(ValidCreateRequest("Dev"));
        var second = await _sut.CreateAsync(ValidCreateRequest("Staging"));

        await _sut.DeleteAsync(first.Id);

        var remaining = await _sut.GetAllAsync();
        Assert.Single(remaining);
        Assert.Equal(second.Id, remaining[0].Id);
        Assert.True(remaining[0].IsDefault);
    }

    [Fact]
    public async Task DeleteAsync_DeletingFinalEnvironment_LeavesZeroEnvironmentsAndZeroDefaults()
    {
        var only = await _sut.CreateAsync(ValidCreateRequest("Dev"));

        await _sut.DeleteAsync(only.Id);

        var remaining = await _sut.GetAllAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EnvironmentNotFoundException>(() => _sut.GetByIdAsync(999));
    }

    [Fact]
    public async Task UpdateAsync_ChangesNameAndBaseUrl_WithoutTouchingDefault()
    {
        var created = await _sut.CreateAsync(ValidCreateRequest("Dev"));

        var updated = await _sut.UpdateAsync(created.Id, new UpdateEnvironmentRequest
        {
            Name = "Dev Renamed",
            BaseUrl = "https://example.com/renamed",
        });

        Assert.Equal("Dev Renamed", updated.Name);
        Assert.Equal("https://example.com/renamed", updated.BaseUrl);
        Assert.True(updated.IsDefault); // untouched by an ordinary edit
    }
}

using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Models;
// "Environment" collides with System.Environment (brought in implicitly via
// ImplicitUsings) — alias it explicitly rather than renaming the entity,
// since the Step 2 spec names it "Environment".
using Environment = OffenderWatch.TestManagement.Server.Models.Environment;

namespace OffenderWatch.TestManagement.Server.Data;

/// <summary>
/// EF Core context for the Part 5 test-management schema. Data-access-only —
/// no query/business logic lives here (Step 2 scope: domain model + schema).
/// </summary>
public class TestManagementDbContext : DbContext
{
    public TestManagementDbContext(DbContextOptions<TestManagementDbContext> options)
        : base(options)
    {
    }

    public DbSet<Environment> Environments => Set<Environment>();

    public DbSet<TestRun> TestRuns => Set<TestRun>();

    public DbSet<TestCase> TestCases => Set<TestCase>();

    public DbSet<ScenarioResult> ScenarioResults => Set<ScenarioResult>();

    public DbSet<EvidenceArtifact> EvidenceArtifacts => Set<EvidenceArtifact>();

    public DbSet<TestDataRecord> TestDataRecords => Set<TestDataRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureEnvironment(modelBuilder);
        ConfigureTestRun(modelBuilder);
        ConfigureTestCase(modelBuilder);
        ConfigureScenarioResult(modelBuilder);
        ConfigureEvidenceArtifact(modelBuilder);
        ConfigureTestDataRecord(modelBuilder);
    }

    private static void ConfigureEnvironment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Environment>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.BaseUrl).IsRequired().HasMaxLength(500);

            // Name must be unique (2.1).
            entity.HasIndex(e => e.Name).IsUnique();

            // Environment 1 -> many TestRuns. EnvironmentId on TestRun is
            // nullable and SetNull on delete specifically so deleting an
            // Environment can never cascade-delete historical TestRuns —
            // the run keeps its EnvironmentNameSnapshot/BaseUrlSnapshot
            // regardless of what happens to this row.
            entity.HasMany(e => e.TestRuns)
                .WithOne(r => r.Environment)
                .HasForeignKey(r => r.EnvironmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureTestRun(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestRun>(entity =>
        {
            entity.Property(r => r.EnvironmentNameSnapshot).IsRequired().HasMaxLength(200);
            entity.Property(r => r.BaseUrlSnapshot).IsRequired().HasMaxLength(500);

            // Stored as strings (not ints) so the raw SQLite file stays
            // directly readable/explainable without a lookup table.
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(20).IsRequired();

            // TestRun 1 -> many ScenarioResults / TestDataRecords. Restrict
            // (not Cascade): a TestRun is never expected to be deleted in
            // normal operation, and Restrict makes an accidental attempt
            // fail loudly instead of silently wiping historical results.
            entity.HasMany(r => r.ScenarioResults)
                .WithOne(sr => sr.TestRun)
                .HasForeignKey(sr => sr.TestRunId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(r => r.TestDataRecords)
                .WithOne(td => td.TestRun)
                .HasForeignKey(td => td.TestRunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTestCase(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestCase>(entity =>
        {
            entity.Property(t => t.ExternalId).IsRequired().HasMaxLength(500);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(500);
            entity.Property(t => t.RequirementId).HasMaxLength(50);
            entity.Property(t => t.BugId).HasMaxLength(50);
            entity.Property(t => t.Suite).HasConversion<string>().HasMaxLength(10).IsRequired();

            // ExternalId must be unique (2.3) — the same TestCase row is
            // reused across every run of that test, which is what makes
            // TM-04 history possible without a separate history table.
            entity.HasIndex(t => t.ExternalId).IsUnique();

            // TestCase 1 -> many ScenarioResults. Restrict: a TestCase must
            // keep existing for as long as any ScenarioResult references
            // it, since history is derived by querying ScenarioResults per
            // TestCase.
            entity.HasMany(t => t.ScenarioResults)
                .WithOne(sr => sr.TestCase)
                .HasForeignKey(sr => sr.TestCaseId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureScenarioResult(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScenarioResult>(entity =>
        {
            entity.Property(sr => sr.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // One TestCase has at most one ScenarioResult per TestRun (2.4).
            entity.HasIndex(sr => new { sr.TestRunId, sr.TestCaseId }).IsUnique();

            // ScenarioResult 1 -> many EvidenceArtifacts. Cascade is
            // appropriate here (unlike TestRun/TestCase above): evidence
            // only ever exists in the context of its ScenarioResult, so if
            // a ScenarioResult row were ever removed its evidence rows
            // (and the files they point at) should go with it.
            entity.HasMany(sr => sr.EvidenceArtifacts)
                .WithOne(a => a.ScenarioResult)
                .HasForeignKey(a => a.ScenarioResultId)
                .OnDelete(DeleteBehavior.Cascade);

            // ScenarioResult 1 -> many TestDataRecords (optional — see
            // TestDataRecord.ScenarioResultId). SetNull: losing the
            // scenario link must never delete the underlying test-data
            // ownership record itself, since that would break cleanup
            // safety.
            entity.HasMany(sr => sr.TestDataRecords)
                .WithOne(td => td.ScenarioResult)
                .HasForeignKey(td => td.ScenarioResultId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureEvidenceArtifact(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvidenceArtifact>(entity =>
        {
            entity.Property(a => a.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(a => a.RelativePath).IsRequired().HasMaxLength(500);
            entity.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        });
    }

    private static void ConfigureTestDataRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestDataRecord>(entity =>
        {
            entity.Property(td => td.EntityType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(td => td.CleanupStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(td => td.ExternalId).HasMaxLength(100);
            entity.Property(td => td.Identifier).HasMaxLength(200);

            // Index (not unique) to make "everything this run created" and
            // "is this specific app entity already tracked" lookups fast —
            // the future cleanup step will run both kinds of query.
            entity.HasIndex(td => new { td.TestRunId, td.CleanupStatus });
            entity.HasIndex(td => new { td.EntityType, td.ExternalId });
        });
    }
}

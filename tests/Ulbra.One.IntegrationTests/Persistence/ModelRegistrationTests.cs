using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Api.Database;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Common.Infrastructure.Auditing;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Persistence;

namespace Ulbra.One.IntegrationTests.Persistence;

/// <summary>
/// Guard rail for the DI-driven model. A module that stops registering its
/// IModuleModelConfiguration silently drops its entities from the model, and the next
/// "migrations add" would generate DROP TABLE. This test fails first instead.
///
/// It compares the two SOURCES of contributions directly — what the DI container has registered
/// (the AddXModule calls) vs. what ModuleModelConfigurations.Create() lists for design-time —
/// rather than comparing the two built IModel instances. EF Core caches the built model keyed by
/// (context type, designTime), and both AppDbContext instances here are built with
/// designTime: false, so a model-vs-model comparison would silently reuse the runtime model for
/// both sides and never actually exercise the design-time factory's OnModelCreating.
/// </summary>
[Collection("Postgres")]
public class ModelRegistrationTests(PostgresFixture fixture)
{
    [Fact]
    public void RegisteredContributions_MatchDesignTimeList()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var registered = scope.ServiceProvider.GetServices<IModuleModelConfiguration>();

        var designTime = ModuleModelConfigurations.Create();

        var registeredKeys = registered.Select(ContributionKey).OrderBy(k => k).ToArray();
        var designTimeKeys = designTime.Select(ContributionKey).OrderBy(k => k).ToArray();

        var missingFromDesignTime = registeredKeys.Except(designTimeKeys).ToArray();
        var missingFromRuntime = designTimeKeys.Except(registeredKeys).ToArray();

        Assert.True(
            missingFromDesignTime.Length == 0 && missingFromRuntime.Length == 0,
            $"Module model configuration drift detected.\n" +
            $"Registered in DI but missing from ModuleModelConfigurations: [{string.Join(", ", missingFromDesignTime)}]\n" +
            $"Listed in ModuleModelConfigurations but not registered in DI: [{string.Join(", ", missingFromRuntime)}]");
    }

    [Fact]
    public void Model_CoversEverySchema()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var mapped = ctx.Model.GetEntityTypes()
            .Select(t => t.GetSchema())
            .Where(s => s is not null)
            .Distinct()
            .ToHashSet()!;

        string[] expected =
        [
            Schemas.Core, Schemas.Finance, Schemas.Identity, Schemas.Registry, Schemas.Audit,
            Schemas.Accounting, Schemas.Fiscal, Schemas.Budgeting, Schemas.Notifications,
        ];

        var missing = expected.Where(s => !mapped.Contains(s)).ToArray();
        Assert.True(missing.Length == 0, $"No entity mapped for schema(s): {string.Join(", ", missing)}");
    }

    [Fact]
    public void Model_HasNoPendingChanges()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // EF Core 10.0.1 exposes only the synchronous overload (RelationalDatabaseFacadeExtensions);
        // there is no HasPendingModelChangesAsync. It is a pure in-memory model diff, no I/O.
        Assert.False(
            ctx.Database.HasPendingModelChanges(),
            "The EF model no longer matches the committed migrations. Run 'dotnet ef migrations add' " +
            "and inspect the diff — an unintended rename here becomes a destructive migration in production.");
    }

    [Fact]
    public void Interceptors_RunCommonBeforeModuleRegistered()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Read the interceptors the context was configured with (AddInterceptors), not
        // ctx.GetService<IEnumerable<IInterceptor>>() — that resolves EF's own internal
        // interceptor services and does not include the ones wired in PersistenceModule.
        var interceptors = ctx.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!.Interceptors!.ToArray();

        var auditIndex = Array.FindIndex(interceptors, i => i is AuditInterceptor);
        var auditLogIndex = Array.FindIndex(interceptors, i => i is AuditLogInterceptor);

        Assert.True(auditIndex >= 0, "AuditInterceptor is not attached to AppDbContext");
        Assert.True(auditLogIndex >= 0, "AuditLogInterceptor is not attached to AppDbContext");

        // AuditInterceptor stamps CreatedBy/UpdatedBy; AuditLogInterceptor diffs the entries.
        // Reversing them makes the audit log record pre-stamp values.
        Assert.True(auditIndex < auditLogIndex, "AuditInterceptor must run before AuditLogInterceptor");
    }

    /// <summary>Stable, human-readable identity for a contribution: the scanned assembly's full
    /// name for AssemblyModelConfiguration (so the eight scans stay distinguishable from each
    /// other), or the concrete type's full name for anything else (e.g. Audit's typed
    /// contribution).</summary>
    private static string ContributionKey(IModuleModelConfiguration configuration) =>
        configuration is AssemblyModelConfiguration assemblyConfig
            ? $"assembly:{assemblyConfig.Assembly.GetName().Name}"
            : $"type:{configuration.GetType().FullName}";
}

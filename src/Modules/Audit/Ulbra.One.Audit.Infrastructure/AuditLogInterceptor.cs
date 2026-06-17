using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Diagnostics;
using System.Text.Json;
using Ulbra.One.Audit.Domain;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application.Authentication;

namespace Ulbra.One.Audit.Infrastructure;

public sealed class AuditLogInterceptor(UserContext userContext, TimeProvider clock, AuditContext auditContext)
    : SaveChangesInterceptor
{
    private static readonly IReadOnlySet<string> IgnoredColumns =
        new HashSet<string> { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "DeletedAt" };

    // Logs are captured from the business ChangeTracker in SavingChanges, then persisted via the dedicated
    // AuditContext in SavedChanges (after the business INSERT/UPDATE/DELETE commits). Buffered per business
    // context instance so concurrent contexts sharing this scoped interceptor don't cross-contaminate.
    private readonly Dictionary<DbContext, List<AuditLog>> _pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null) Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null) FlushAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken ct = default)
    {
        if (eventData.Context is not null) await FlushAsync(eventData.Context, ct);
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, ct);
    }

    private void Capture(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        var actor = ResolveUserId();
        var now = clock.GetUtcNow();
        var traceId = Activity.Current?.TraceId.ToString();

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var logs = new List<AuditLog>();
        foreach (var entry in entries)
        {
            var table = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
            var schema = entry.Metadata.GetSchema() ?? "";
            var entityType = entry.Metadata.ClrType.Name;
            var pk = SerializePrimaryKey(entry);

            switch (entry.State)
            {
                case EntityState.Added:
                {
                    var current = entry.CurrentValues.Properties.ToDictionary(p => p.Name, p => entry.CurrentValues[p]);
                    foreach (var k in IgnoredColumns) current.Remove(k);
                    logs.Add(new AuditLog(AuditAction.Create, entityType, schema, table, pk,
                        null, JsonSerializer.Serialize(current), null, actor, traceId, now));
                    break;
                }
                case EntityState.Deleted:
                {
                    var original = entry.OriginalValues.Properties.ToDictionary(p => p.Name, p => entry.OriginalValues[p]);
                    foreach (var k in IgnoredColumns) original.Remove(k);
                    logs.Add(new AuditLog(AuditAction.Delete, entityType, schema, table, pk,
                        JsonSerializer.Serialize(original), null, null, actor, traceId, now));
                    break;
                }
                case EntityState.Modified:
                {
                    var original = entry.OriginalValues.Properties.ToDictionary(p => p.Name, p => entry.OriginalValues[p]);
                    var current = entry.CurrentValues.Properties.ToDictionary(p => p.Name, p => entry.CurrentValues[p]);
                    var (oldJson, newJson, affJson, hasChange) = AuditDiff.Compute(original, current, IgnoredColumns);
                    if (hasChange)
                        logs.Add(new AuditLog(AuditAction.Update, entityType, schema, table, pk,
                            oldJson, newJson, affJson, actor, traceId, now));
                    break;
                }
            }
        }

        if (logs.Count == 0) return;
        if (_pending.TryGetValue(context, out var existing)) existing.AddRange(logs);
        else _pending[context] = logs;
    }

    private async Task FlushAsync(DbContext context, CancellationToken ct)
    {
        if (!_pending.Remove(context, out var logs) || logs.Count == 0) return;
        auditContext.AuditLogs.AddRange(logs);
        await auditContext.SaveChangesAsync(ct);
    }

    private static string SerializePrimaryKey(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var pkProps = entry.Metadata.FindPrimaryKey()?.Properties ?? [];
        var dict = pkProps.ToDictionary(p => p.Name, p => entry.Property(p.Name).CurrentValue);
        return dict.Count == 1 ? dict.Values.First()?.ToString() ?? "" : JsonSerializer.Serialize(dict);
    }

    private string ResolveUserId()
    {
        try { return userContext.IsAuthenticated ? userContext.UserId : "system"; }
        catch (InvalidOperationException) { return "system"; }
    }
}

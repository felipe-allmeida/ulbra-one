using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Diagnostics;
using System.Text.Json;
using Ulbra.One.Audit.Domain;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application.Authentication;

namespace Ulbra.One.Audit.Infrastructure;

public sealed class AuditLogInterceptor(UserContext userContext, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly IReadOnlySet<string> IgnoredColumns =
        new HashSet<string> { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "DeletedAt" };

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
        foreach (var log in logs) context.Add(log);
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

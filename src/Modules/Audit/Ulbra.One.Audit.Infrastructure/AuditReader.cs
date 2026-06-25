using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Ulbra.One.Audit.Domain;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application.Auditing;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Audit.Infrastructure;

/// <summary>
/// Reads change timestamps from <see cref="AuditLog"/> rows without exposing the Audit domain
/// outside its own module. Satisfies <see cref="IAuditReader"/> for cross-module consumers.
/// </summary>
public sealed class AuditReader([FromKeyedServices("audit")] IUnitOfWork uow) : IAuditReader
{
    public async Task<DateTimeOffset?> GetLatestChangeAsync(
        string entityType, string primaryKey, string affectedColumn, CancellationToken ct)
    {
        // Fetch only Update rows for the exact entity; membership check done in memory to avoid
        // fragile LIKE '%col%' jsonb substring matching.
        var rows = await uow.ReadSet<AuditLog>()
            .Where(a => a.EntityType == entityType
                        && a.PrimaryKey == primaryKey
                        && a.Action == AuditAction.Update)
            .Select(a => new { a.OccurredAt, a.AffectedColumns })
            .ToListAsync(ct);

        return rows
            .Where(r => ContainsColumn(r.AffectedColumns, affectedColumn))
            .Select(r => (DateTimeOffset?)r.OccurredAt)
            .Max();
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetLatestChangesAsync(
        string entityType, IReadOnlyCollection<string> primaryKeys, string affectedColumn, CancellationToken ct)
    {
        if (primaryKeys.Count == 0)
            return new Dictionary<string, DateTimeOffset>();

        var keys = primaryKeys.ToList();
        var rows = await uow.ReadSet<AuditLog>()
            .Where(a => a.EntityType == entityType
                        && keys.Contains(a.PrimaryKey)
                        && a.Action == AuditAction.Update)
            .Select(a => new { a.PrimaryKey, a.OccurredAt, a.AffectedColumns })
            .ToListAsync(ct);

        return rows
            .Where(r => ContainsColumn(r.AffectedColumns, affectedColumn))
            .GroupBy(r => r.PrimaryKey)
            .ToDictionary(g => g.Key, g => g.Max(r => r.OccurredAt));
    }

    private static bool ContainsColumn(string? affectedColumns, string column)
    {
        if (string.IsNullOrEmpty(affectedColumns))
            return false;

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(affectedColumns);
            return list is not null && list.Contains(column);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

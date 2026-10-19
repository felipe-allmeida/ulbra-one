namespace Ulbra.One.Common.Application.Persistence;

/// <summary>
/// Single, non-keyed unit of work over the shared <c>AppDbContext</c>. Inject directly as
/// <c>IUnitOfWork</c> — no <c>[FromKeyedServices]</c> required. The <c>Audit</c> module is the
/// deliberate exception: it uses its own <c>AuditContext</c> registered under the keyed name
/// <c>"audit"</c>.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>No-tracking query set — use for reads.</summary>
    IQueryable<T> ReadSet<T>() where T : class;

    /// <summary>Tracked query set — use when the entity will be mutated.</summary>
    IQueryable<T> Set<T>() where T : class;

    Task AddAsync<T>(T entity, CancellationToken ct) where T : class;

    void Remove<T>(T entity) where T : class;

    void RemoveRange<T>(IEnumerable<T> entities) where T : class;

    Task<int> SaveChangesAsync(CancellationToken ct);

    /// <summary>Detaches all tracked entities, discarding pending in-memory changes. Use to recover a
    /// clean state before re-fetching and retrying after a concurrency conflict.</summary>
    void ClearChangeTracker();
}

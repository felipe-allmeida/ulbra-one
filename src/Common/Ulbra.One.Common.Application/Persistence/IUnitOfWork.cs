namespace Ulbra.One.Common.Application.Persistence;

/// <summary>
/// Per-module unit of work over the module's DbContext. Endpoints inject it keyed by module
/// (e.g. <c>[FromKeyedServices("core")]</c>) so the right context is bound.
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
}

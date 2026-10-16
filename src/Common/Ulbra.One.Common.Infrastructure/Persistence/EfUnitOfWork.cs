using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Common.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> over a single <typeparamref name="TContext"/>.
/// Registered as a single non-keyed service backed by <c>AppDbContext</c>. The <c>Audit</c> module
/// is the deliberate exception — it registers a keyed <c>"audit"</c> instance over
/// <c>AuditContext</c> for its separate append-only writes.
/// </summary>
public sealed class EfUnitOfWork<TContext>(TContext db) : IUnitOfWork
    where TContext : DbContext
{
    public IQueryable<T> ReadSet<T>() where T : class => db.Set<T>().AsNoTracking();

    public IQueryable<T> Set<T>() where T : class => db.Set<T>();

    public async Task AddAsync<T>(T entity, CancellationToken ct) where T : class =>
        await db.Set<T>().AddAsync(entity, ct);

    public void Remove<T>(T entity) where T : class => db.Set<T>().Remove(entity);

    public void RemoveRange<T>(IEnumerable<T> entities) where T : class => db.Set<T>().RemoveRange(entities);

    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public void ClearChangeTracker() => db.ChangeTracker.Clear();
}

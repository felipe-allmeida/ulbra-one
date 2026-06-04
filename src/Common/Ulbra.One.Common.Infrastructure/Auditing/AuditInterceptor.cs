using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Common.Infrastructure.Authentication;

namespace Ulbra.One.Common.Infrastructure.Auditing;

public sealed class AuditInterceptor(UserContext userContext, TimeProvider timeProvider) : SaveChangesInterceptor
{
    public static void ApplyCreated(IAuditable entity, string userId, TimeProvider time) =>
        entity.MarkCreated(userId, time.GetUtcNow());

    public static void ApplyUpdated(IAuditable entity, string userId, TimeProvider time) =>
        entity.MarkUpdated(userId, time.GetUtcNow());

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is not null) Apply(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        var userId = ResolveUserId();
        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added) ApplyCreated(entry.Entity, userId, timeProvider);
            else if (entry.State == EntityState.Modified) ApplyUpdated(entry.Entity, userId, timeProvider);
        }
    }

    private string ResolveUserId()
    {
        try { return userContext.IsAuthenticated ? userContext.UserId : "system"; }
        catch { return "system"; }
    }
}

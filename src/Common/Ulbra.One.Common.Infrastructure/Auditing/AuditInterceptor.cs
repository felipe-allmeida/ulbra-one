using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Common.Application.Authentication;

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

    // Falls back to "system" outside an authenticated request (e.g. startup migrations,
    // background jobs). The IsAuthenticated guard is the normal path; the narrow catch only
    // covers UserId throwing if there's no HttpContext at all — we don't swallow anything else.
    private string ResolveUserId()
    {
        try { return userContext.IsAuthenticated ? userContext.UserId : "system"; }
        catch (InvalidOperationException) { return "system"; }
    }
}

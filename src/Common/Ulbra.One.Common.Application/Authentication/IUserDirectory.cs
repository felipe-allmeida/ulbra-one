namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Resolves user display names by id without forcing callers to reference the Identity module's
/// domain. Implemented in Identity.Infrastructure; consumed across modules (audit trails, activity feeds).
/// </summary>
public interface IUserDirectory
{
    /// <summary>Maps each known user id to its display name. Unknown ids are omitted from the result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}

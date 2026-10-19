namespace Ulbra.One.Common.Application.Authentication;

/// <summary>How a user is shown next to something they did: display name plus the Google profile photo.
/// <see cref="PictureUrl"/> is <c>null</c> for a user with no photo (or who never signed in) — render the
/// initials of <see cref="Name"/> instead.</summary>
public sealed record UserProfile(string Name, string? PictureUrl);

/// <summary>
/// Resolves user display profiles by id without forcing callers to reference the Identity module's
/// domain. Implemented in Identity.Infrastructure; consumed across modules (audit trails, activity feeds).
/// </summary>
public interface IUserDirectory
{
    /// <summary>Maps each known user id to its display profile. Unknown ids are omitted from the result.</summary>
    Task<IReadOnlyDictionary<Guid, UserProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}

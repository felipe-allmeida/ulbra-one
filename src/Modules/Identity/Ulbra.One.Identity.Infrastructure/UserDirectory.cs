using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure;

/// <summary>Reads user display profiles from the single AppDbContext via the shared unit of work.</summary>
public sealed class UserDirectory(IUnitOfWork uow) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserProfile>> GetProfilesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, UserProfile>();
        var distinct = userIds.Distinct().ToList();
        return await uow.ReadSet<User>()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserProfile(u.Name, u.PictureUrl), ct);
    }
}

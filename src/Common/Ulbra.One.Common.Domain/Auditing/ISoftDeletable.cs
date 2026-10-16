namespace Ulbra.One.Common.Domain.Auditing;

/// <summary>Entity that is soft-deleted (LGPD): a deletion timestamp instead of a physical delete.</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }

    void MarkDeleted(DateTimeOffset at);
}

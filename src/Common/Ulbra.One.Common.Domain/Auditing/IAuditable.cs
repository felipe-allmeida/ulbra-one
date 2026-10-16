namespace Ulbra.One.Common.Domain.Auditing;

/// <summary>Entity whose audit fields are filled automatically on save.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }
    string CreatedBy { get; }
    DateTimeOffset UpdatedAt { get; }
    string UpdatedBy { get; }

    void MarkCreated(string userId, DateTimeOffset at);
    void MarkUpdated(string userId, DateTimeOffset at);
}

namespace Ulbra.One.Common.Domain;

/// <summary>Well-known actor id stamped on records created by background jobs (no real user).</summary>
public static class SystemActor
{
    public static readonly Guid Id = new("00000000-0000-0000-0000-0000000005ed");
}

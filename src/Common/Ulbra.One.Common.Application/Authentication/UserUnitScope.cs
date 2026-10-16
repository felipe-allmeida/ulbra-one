namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// A finer data-access scope than <see cref="UserScope"/>: a specific unit within a granted company
/// (AEL-883). Sibling of the company scope — explicit grants only (no "empty = all"): a scoped user needs the
/// unit explicitly granted, an empty unit scope blocks. Carried in the JWT as <see cref="UnitScopeClaim"/>.
/// </summary>
public readonly record struct UserUnitScope(Guid CompanyId, Guid UnitId);

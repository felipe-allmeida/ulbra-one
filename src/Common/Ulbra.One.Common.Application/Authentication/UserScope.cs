namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Access scope granted to the user: one company (legal entity). It is the unit of per-resource
/// access control (see AEL-764). Prepared to gain per-unit granularity in the future without
/// breaking the current contract.
/// </summary>
public readonly record struct UserScope(Guid CompanyId);

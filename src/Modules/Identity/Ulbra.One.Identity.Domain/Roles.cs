namespace Ulbra.One.Identity.Domain;

/// <summary>
/// Names of the seeded roles. The user stores the role by name (<c>User.Role</c>);
/// each role's permissions live in <c>Role.Claims</c> and are seeded in the Infrastructure.
/// <c>Admin</c> is a superuser (global permission bypass).
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Finance = "Finance";
    public const string Controllership = "Controllership";
    public const string Accounting = "Accounting";

    /// <summary>Presidency (AEL-883): an executive role that does almost everything the Admin can — it holds
    /// every functional permission EXCEPT <c>admin:users</c> (so it cannot manage users or role permissions) —
    /// and, like Admin, bypasses the Empresa × Unidade × Centro de Custo scope (sees all). Unlike Admin it is
    /// permission-driven (not a global bypass), which is what lets <c>admin:users</c> be withheld.</summary>
    public const string Presidency = "Presidency";

    /// <summary>Requester in the requisition workflow (creates and tracks their own requisitions).
    /// Dedicated role brought forward by AEL-796; the canonical role catalog is consolidated by AEL-771.</summary>
    public const string Requester = "Requester";

    /// <summary>Approver: final approver of the requisition workflow (AEL-842, AEL-883).
    /// Bound to units/cost centers — approves only requisitions of its bound
    /// scope. Approves without spending authority (no alçada) and without budget override — overruns go to
    /// the Adequação flow.</summary>
    public const string Approver = "Approver";

    /// <summary>Tax role: validates a Serviço/Conta Fixa payable's withholdings (edits/adds/removes the withholding
    /// types + rates) during the blocking tax-analysis stage and releases it for payment. Appears in the flow after
    /// the Approver's approval; while a payable is pending tax analysis it can be neither paid nor posted.</summary>
    public const string Tax = "Tax";
}

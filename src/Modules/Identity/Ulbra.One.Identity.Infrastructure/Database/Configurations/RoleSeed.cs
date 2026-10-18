using System.Linq;
using Ulbra.One.Common.Application.Authentication;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

/// <summary>Seed data for roles and their permissions. Fixed Guids so <c>HasData</c> is deterministic; <c>Admin</c>
/// has no claims (global bypass). Separation of duties: Controllership triages and the Approver gives the final
/// approval (no alçada; overruns go to the Adequação workflow, not an override).</summary>
internal static class RoleSeed
{
    public static readonly Guid AdminId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserId = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid FinanceId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ControllershipId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid AccountingId = new("44444444-4444-4444-4444-444444444444");
    public static readonly Guid RequesterId = new("55555555-5555-5555-5555-555555555555");
    public static readonly Guid ApproverId = new("66666666-6666-6666-6666-666666666666");
    public static readonly Guid PresidencyId = new("77777777-7777-7777-7777-777777777777");
    public static readonly Guid TaxId = new("88888888-8888-8888-8888-888888888888");

    /// <summary>Permissions per role (<c>role_claim</c> rows), except Admin (bypass).</summary>
    private static readonly (Guid RoleId, string[] Permissions)[] Grants =
    [
        (FinanceId,
        [
            Permissions.PayablesRead, Permissions.PayablesWrite, Permissions.PayablesPay,
            Permissions.ReceivablesRead, Permissions.ReceivablesWrite, Permissions.ReceivablesReceive,
            Permissions.TreasuryRead, Permissions.TreasuryWrite,
            Permissions.RegistryRead, Permissions.RegistryWrite,
            Permissions.CoreRead, Permissions.ReportsRead,
            Permissions.RequisitionsRead, Permissions.RequisitionsWrite,
            Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsWrite,
            Permissions.ProjectsRead,
        ]),
        (ControllershipId,
        [
            Permissions.PayablesRead, Permissions.PayablesApprove,
            Permissions.ReceivablesRead,
            Permissions.TreasuryRead, Permissions.TreasuryApprove,
            Permissions.AccountingRead, Permissions.FiscalRead,
            // registry:write to correct the vendor's registration during the requisition's triage.
            Permissions.RegistryRead, Permissions.RegistryWrite,
            Permissions.CoreRead, Permissions.CoreWrite,
            // Controllership = Triage of the requisition workflow (AEL-842); the final approval belongs to the Approver.
            // No write/approve/override — does not create, approve, or breach the ceiling. Provisional mapping — AEL-771.
            Permissions.RequisitionsRead, Permissions.RequisitionsTriage,
            // Controllership triages AND approves the adequação by default (approval still gated per-user by
            // CanApproveBudgetAdjustment, AEL-883). Triage is shared with Accounting. Provisional mapping — AEL-771.
            Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsWrite,
            Permissions.BudgetAdjustmentsTriage, Permissions.BudgetAdjustmentsApprove,
            Permissions.ProjectsRead, Permissions.ProjectsWrite,
            Permissions.ReportsRead, Permissions.AuditRead,
        ]),
        (AccountingId,
        [
            Permissions.AccountingRead, Permissions.AccountingWrite, Permissions.AccountingPost,
            Permissions.FiscalRead, Permissions.FiscalWrite,
            Permissions.ReportsRead,
            // payables:write enables the Expense Registration / Classification flow (post,
            // classify, attach the invoice) — all backed by payables:write in the backend. It is the
            // Accounting team's work over the expense; it does not grant approval/payment (payables:approve/pay).
            Permissions.PayablesRead, Permissions.PayablesWrite, Permissions.ReceivablesRead,
            Permissions.RegistryRead, Permissions.CoreRead,
            // Accounting only sees the requisition (read); the workflow triage moved to the Controllership (AEL-842).
            Permissions.RequisitionsRead,
            // Accounting still does the triage of the adequação. Provisional mapping — AEL-771.
            Permissions.BudgetAdjustmentsRead, Permissions.BudgetAdjustmentsTriage,
            Permissions.ProjectsRead,
        ]),
        (RequesterId,
        [
            // Requester = start of the workflow: creates and tracks their own requisitions.
            // No accounting/payables WRITE (does not pay, does not post). The role-based landing keys on
            // write-level signatures (accounting:write/post, payables:approve, payables:tax, ...), so the
            // reads below leave the requester landing on its own dashboard. Provisional mapping — AEL-771.
            Permissions.CoreRead,
            // registry:write so the requester can register a vendor inline from the requisition form
            // (parity with the legacy app, where the requester types the vendor CNPJ + name). The
            // requester UI only surfaces vendor creation — customers/employees stay out of their screens.
            Permissions.RegistryRead, Permissions.RegistryWrite,
            // Reference reads the vendor form and the tax step of a service/conta-fixa requisition perform:
            // the ledger accounts to associate, and the withholding preview. Without them the screen 403s and
            // silently saves a vendor with no ledger account and no withholding — observed in production.
            Permissions.AccountingRead, Permissions.FiscalRead,
            Permissions.ProjectsRead,
            Permissions.RequisitionsRead, Permissions.RequisitionsWrite,
        ]),
        (ApproverId,
        [
            // Approver (Aprovador, AEL-883) = final approver of the requisition
            // workflow (AEL-842). Approves the requisition but has NO spending authority (no alçada) and
            // CANNOT override the budget ceiling — overruns route to the Adequação flow instead. Approves
            // only requisitions of its bound units/cost centers (enforced live at approve/inbox time).
            Permissions.RequisitionsRead, Permissions.RequisitionsApprove,
            // SLA analysis report (AEL-872); ADM sees it via the global bypass.
            Permissions.RequisitionsSlaReport,
            Permissions.CoreRead,
            Permissions.RegistryRead,
            Permissions.ProjectsRead,
        ]),
        (TaxId,
        [
            // Tax role: owns tax analysis. payables:tax edits the withholdings (SetWithholdings) and releases the
            // payable from PendingTaxAnalysis. Reads the payable, the withholding rules (fiscal:read) and the
            // vendor's tax profile (registry:read) to validate — but does not maintain the rules/vendor itself.
            Permissions.PayablesTax, Permissions.PayablesRead,
            Permissions.FiscalRead, Permissions.RegistryRead,
            Permissions.CoreRead, Permissions.ReportsRead,
        ]),
        // Presidency (AEL-883): every functional permission EXCEPT admin:users (so it cannot manage users or
        // role permissions). Derived from the catalog so it stays "almost Admin" as new permissions are added.
        // Its scope bypass (sees all companies/units, approves without the per-user adequação grant) is in
        // UserContext.IsPresident, not here.
        (PresidencyId, [.. Permissions.All.Where(p => p != Permissions.AdminUsers)]),
    ];

    /// <summary>Seed rows for the <c>role</c> table (scalars only — EF anonymous shape).</summary>
    public static IEnumerable<object> Roles() =>
    [
        new { Id = AdminId, Name = Identity.Domain.Roles.Admin },
        new { Id = UserId, Name = Identity.Domain.Roles.User },
        new { Id = FinanceId, Name = Identity.Domain.Roles.Finance },
        new { Id = ControllershipId, Name = Identity.Domain.Roles.Controllership },
        new { Id = AccountingId, Name = Identity.Domain.Roles.Accounting },
        new { Id = RequesterId, Name = Identity.Domain.Roles.Requester },
        new { Id = ApproverId, Name = Identity.Domain.Roles.Approver },
        new { Id = PresidencyId, Name = Identity.Domain.Roles.Presidency },
        new { Id = TaxId, Name = Identity.Domain.Roles.Tax },
    ];

    /// <summary>Seed rows for the <c>role_claim</c> table (composite key RoleId+Permission).</summary>
    public static IEnumerable<object> Claims()
    {
        foreach (var (roleId, permissions) in Grants)
            foreach (var permission in permissions)
                yield return new { RoleId = roleId, Permission = permission };
    }
}

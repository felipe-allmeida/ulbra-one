using Ulbra.One.Common.Application.Authentication;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

/// <summary>
/// Dados de seed dos cargos e suas permissões. Guids fixos para o <c>HasData</c> ser
/// determinístico entre migrations. <c>Admin</c> não recebe claims — é bypass global.
/// Segregação de funções: Financeiro paga mas não aprova; Controladoria aprova mas não paga.
/// </summary>
internal static class RoleSeed
{
    public static readonly Guid AdminId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserId = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid FinanceId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ControllershipId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid AccountingId = new("44444444-4444-4444-4444-444444444444");

    /// <summary>Permissões por cargo (linhas de <c>role_claim</c>), exceto Admin (bypass).</summary>
    private static readonly (Guid RoleId, string[] Permissions)[] Grants =
    [
        (FinanceId,
        [
            Permissions.PayablesRead, Permissions.PayablesWrite, Permissions.PayablesPay,
            Permissions.ReceivablesRead, Permissions.ReceivablesWrite, Permissions.ReceivablesReceive,
            Permissions.TreasuryRead, Permissions.TreasuryWrite,
            Permissions.RegistryRead, Permissions.RegistryWrite,
            Permissions.CoreRead, Permissions.ReportsRead,
        ]),
        (ControllershipId,
        [
            Permissions.PayablesRead, Permissions.PayablesApprove,
            Permissions.ReceivablesRead,
            Permissions.TreasuryRead, Permissions.TreasuryApprove,
            Permissions.AccountingRead, Permissions.FiscalRead,
            Permissions.RegistryRead,
            Permissions.CoreRead, Permissions.CoreWrite,
            Permissions.ReportsRead, Permissions.AuditRead,
        ]),
        (AccountingId,
        [
            Permissions.AccountingRead, Permissions.AccountingWrite, Permissions.AccountingPost,
            Permissions.FiscalRead, Permissions.FiscalWrite,
            Permissions.ReportsRead,
            // payables:write habilita o fluxo de Registro de Despesa / Classificação (contabilizar,
            // classificar, anexar NF) — todos backed por payables:write no backend. É o trabalho do
            // time Contábil sobre a despesa; não confere aprovação/pagamento (payables:approve/pay).
            Permissions.PayablesRead, Permissions.PayablesWrite, Permissions.ReceivablesRead,
            Permissions.RegistryRead, Permissions.CoreRead,
        ]),
    ];

    /// <summary>Linhas de seed da tabela <c>role</c> (apenas escalares — shape anônimo do EF).</summary>
    public static IEnumerable<object> Roles() =>
    [
        new { Id = AdminId, Name = Identity.Domain.Roles.Admin },
        new { Id = UserId, Name = Identity.Domain.Roles.User },
        new { Id = FinanceId, Name = Identity.Domain.Roles.Finance },
        new { Id = ControllershipId, Name = Identity.Domain.Roles.Controllership },
        new { Id = AccountingId, Name = Identity.Domain.Roles.Accounting },
    ];

    /// <summary>Linhas de seed da tabela <c>role_claim</c> (chave composta RoleId+Permission).</summary>
    public static IEnumerable<object> Claims()
    {
        foreach (var (roleId, permissions) in Grants)
            foreach (var permission in permissions)
                yield return new { RoleId = roleId, Permission = permission };
    }
}

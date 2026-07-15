namespace Ulbra.One.Common.Application.Authentication;

/// <summary>
/// Catálogo central de permissões — claims do tipo <see cref="ClaimType"/> no formato
/// <c>recurso:ação</c>. Cargos (Roles) compõem subconjuntos destas permissões e endpoints
/// exigem uma permissão via <c>.RequirePermission(...)</c>. É vocabulário de autorização
/// compartilhado entre módulos, por isso vive em Common (referência por constante — nunca
/// um tipo cross-module).
/// </summary>
public static class Permissions
{
    /// <summary>Tipo da claim carregada no JWT para cada permissão concedida.</summary>
    public const string ClaimType = "permission";

    // Cadastros base (Core): empresas, unidades, centros de custo.
    public const string CoreRead = "core:read";
    public const string CoreWrite = "core:write";

    // Cadastros de terceiros (Registry): fornecedores, clientes, colaboradores.
    public const string RegistryRead = "registry:read";
    public const string RegistryWrite = "registry:write";

    // Contas a pagar (Finance/Payables).
    public const string PayablesRead = "payables:read";
    public const string PayablesWrite = "payables:write";
    public const string PayablesApprove = "payables:approve";
    public const string PayablesPay = "payables:pay";

    // Contas a receber (Finance/Receivables).
    public const string ReceivablesRead = "receivables:read";
    public const string ReceivablesWrite = "receivables:write";
    public const string ReceivablesReceive = "receivables:receive";

    // Tesouraria: bancos, conciliação, remessas, fluxo de caixa, transações.
    public const string TreasuryRead = "treasury:read";
    public const string TreasuryWrite = "treasury:write";
    public const string TreasuryApprove = "treasury:approve";

    // Contábil (Accounting): lançamentos, plano de contas, postagens.
    public const string AccountingRead = "accounting:read";
    public const string AccountingWrite = "accounting:write";
    public const string AccountingPost = "accounting:post";

    // Fiscal: apuração/EFD-Reinf, regras de retenção.
    public const string FiscalRead = "fiscal:read";
    public const string FiscalWrite = "fiscal:write";

    // Relatórios gerenciais (balanço, DRE, balancete, aging, retenções).
    public const string ReportsRead = "reports:read";

    // Administração.
    public const string AdminUsers = "admin:users";
    public const string AuditRead = "audit:read";

    /// <summary>Todas as permissões conhecidas (usada para validar/registrar policies).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        CoreRead, CoreWrite,
        RegistryRead, RegistryWrite,
        PayablesRead, PayablesWrite, PayablesApprove, PayablesPay,
        ReceivablesRead, ReceivablesWrite, ReceivablesReceive,
        TreasuryRead, TreasuryWrite, TreasuryApprove,
        AccountingRead, AccountingWrite, AccountingPost,
        FiscalRead, FiscalWrite,
        ReportsRead,
        AdminUsers, AuditRead,
    ];
}

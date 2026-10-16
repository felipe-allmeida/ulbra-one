using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Endpoints.Payables;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.BankAccounts.CreateBankAccount;

public sealed class CreateBankAccount : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Company that owns the account.")]
        public CompanyId CompanyId { get; set; }
        [SwaggerSchema("Bank code (codigo de compensacao), three digits.")]
        [SwaggerExample("033")]
        [NormalizeText] public string BankCode { get; set; } = string.Empty;
        [SwaggerSchema("Bank name, for display.")]
        [SwaggerExample("Banco Santander")]
        [NormalizeText] public string BankName { get; set; } = string.Empty;
        [SwaggerSchema("Branch number (agencia), without the check digit.")]
        [SwaggerExample("1234")]
        [NormalizeText] public string Agency { get; set; } = string.Empty;
        [SwaggerSchema("Account number, without the check digit.")]
        [SwaggerExample("56789")]
        [NormalizeText] public string AccountNumber { get; set; } = string.Empty;
        [SwaggerSchema("Account check digit.")]
        [SwaggerExample("0")]
        [NormalizeText] public string AccountDigit { get; set; } = string.Empty;
        [SwaggerSchema("Kind of account — checking, savings and so on.")]
        public BankAccountType AccountType { get; set; }
        [SwaggerSchema("Short name shown in the account pickers.")]
        [SwaggerExample("Santander - Movimento")]
        [NormalizeText] public string Alias { get; set; } = string.Empty;
        [SwaggerSchema("Balance the account started with, in BRL. The cash-flow projection anchors on the "
            + "reconciled balance instead, so this is a starting point, not the live figure.")]
        [SwaggerExample("0.00")]
        public decimal? OpeningBalance { get; set; }
        [SwaggerSchema("Use this account when a payable does not name one of its own. Only one per company "
            + "should carry the flag. The CNAB payment method is NOT set here — it comes from each "
            + "installment/payable.")]
        public bool IsDefaultForPayments { get; set; }

        /// <summary>CNAB payment-remittance agreement code (convênio). Required to generate a remittance for this account.</summary>
        [SwaggerSchema("CNAB agreement code (convenio) given by the bank. REQUIRED to generate a payment "
            + "remittance from this account — without it the account can receive and pay manually but "
            + "cannot produce a CNAB file.")]
        public string CnabAgreementCode { get; set; } = string.Empty;

        /// <summary>Accounting ledger account that represents this bank account (the credit account when settling a
        /// payable). Required on create.</summary>
        [SwaggerSchema("Ledger account that represents this bank account — the credit side when a payable is "
            + "settled. Required on create; left null on update the settlement falls back to the global "
            + "cash code.")]
        public LedgerAccountId? LedgerAccountId { get; set; }

        /// <summary>OFX account identifier (BANKACCTFROM/ACCTID) used to warn when an imported statement belongs to
        /// another account. Optional.</summary>
        [SwaggerSchema("OFX account identifier (ACCTID). Used to warn when an imported statement actually "
            + "belongs to another account. Optional.")]
        [NormalizeText] public string OfxAccountId { get; set; } = string.Empty;

        // CNAB remittance-header profile (optional): agency check digit + paying-company address.
        [SwaggerSchema("Branch check digit. Part of the optional CNAB remittance header profile.")]
        [NormalizeText] public string AgencyDigit { get; set; } = string.Empty;
        [SwaggerSchema("Paying company street, for the CNAB remittance header. Optional.")]
        [NormalizeText] public string AddressStreet { get; set; } = string.Empty;
        [SwaggerSchema("Paying company street number, for the CNAB remittance header. Optional.")]
        [NormalizeText] public string AddressNumber { get; set; } = string.Empty;
        [SwaggerSchema("Paying company address complement, for the CNAB remittance header. Optional.")]
        [NormalizeText] public string AddressComplement { get; set; } = string.Empty;
        [SwaggerSchema("Paying company city, for the CNAB remittance header. Optional.")]
        [NormalizeText] public string AddressCity { get; set; } = string.Empty;
        [SwaggerSchema("Paying company postal code, for the CNAB remittance header. Optional.")]
        [SwaggerExample("90420-111")]
        [NormalizeText] public string AddressCep { get; set; } = string.Empty;
        [SwaggerSchema("Paying company state (UF), for the CNAB remittance header. Optional.")]
        [SwaggerExample("RS")]
        [NormalizeText] public string AddressState { get; set; } = string.Empty;

        // CNAB payer identity override (optional): empty falls back to the owning company's CNPJ/legal name.
        [SwaggerSchema("Override the payer CNPJ used in the CNAB file. Empty falls back to the owning "
            + "company\u0027s CNPJ.")]
        [NormalizeText] public string PayerCnpj { get; set; } = string.Empty;
        [SwaggerSchema("Override the payer legal name used in the CNAB file. Empty falls back to the owning "
            + "company\u0027s legal name.")]
        [NormalizeText] public string PayerLegalName { get; set; } = string.Empty;
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreateBankAccount")
            .WithSummary("Creates a bank account.")
            .WithDescription("Registers an account for paying and receiving. Two fields decide what the account can do "
                + "downstream: the CNAB agreement code is REQUIRED to generate a remittance from it, and the "
                + "ledger account is what settlements are credited against.").WithTags("Finance")
            .RequirePermission(Permissions.TreasuryWrite).WithRequestNormalizer<Request>()
            .Produces<BankAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Alias))
            return ErrorResults.InvalidRequest("Alias is required.", ErrorCodes.BankAccount.NameRequired);
        if (string.IsNullOrWhiteSpace(req.BankCode) || string.IsNullOrWhiteSpace(req.Agency) || string.IsNullOrWhiteSpace(req.AccountNumber))
            return ErrorResults.InvalidRequest("Bank code, agency and account number are required.", ErrorCodes.BankAccount.Invalid);
        if (!string.IsNullOrWhiteSpace(req.PayerCnpj) && !DocumentValidator.IsValidCnpj(req.PayerCnpj))
            return ErrorResults.InvalidRequest("The payer CNPJ is invalid.", ErrorCodes.BankAccount.InvalidPayerCnpj);

        // The accounting account is required on create: it is the credit account when settling a payable.
        if (req.LedgerAccountId is not { } ledgerId || ledgerId.Value == Guid.Empty)
            return ErrorResults.InvalidRequest("The accounting account is required.", ErrorCodes.BankAccount.LedgerAccountRequired);
        var ledgerProblem = await ClassificationValidation.ValidateAsync(uow, ledgerId.Value, ct);
        if (ledgerProblem is not null)
            return ErrorResults.InvalidRequest("The accounting account is invalid.", ledgerProblem);

        var companyId = req.CompanyId.Value;
        var duplicate = await uow.ReadSet<BankAccount>().AnyAsync(a =>
            a.CompanyId == companyId && a.BankCode == req.BankCode && a.Agency == req.Agency &&
            a.AccountNumber == req.AccountNumber && a.AccountDigit == req.AccountDigit, ct);
        if (duplicate)
            return ErrorResults.InvalidRequest("A bank account with these details already exists.", ErrorCodes.BankAccount.Duplicate);

        var acc = new BankAccount(companyId, req.BankCode, req.BankName, req.Agency, req.AccountNumber,
            req.AccountDigit, req.AccountType, req.Alias, req.OpeningBalance, req.CnabAgreementCode, ledgerId.Value, req.OfxAccountId);
        acc.SetRemittanceProfile(req.AgencyDigit, req.AddressStreet, req.AddressNumber, req.AddressComplement,
            req.AddressCity, req.AddressCep, req.AddressState);
        acc.SetPayerIdentity(req.PayerCnpj, req.PayerLegalName);

        if (req.IsDefaultForPayments)
        {
            // Clear any existing default for this company first (DB unique index is the backstop).
            var existingDefaults = await uow.Set<BankAccount>()
                .Where(b => b.CompanyId == companyId && b.IsDefaultForPayments)
                .ToListAsync(ct);
            foreach (var other in existingDefaults)
                other.ClearDefaultPayer();
            acc.SetAsDefaultPayer();
        }

        await uow.AddAsync(acc, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/bank-accounts/{new BankAccountId(acc.Id)}", BankAccountResponse.From(acc));
    }
}

using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A bank account owned by a company, used as the outflow account when settling payables.</summary>
public class BankAccount : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string BankCode { get; private set; } = string.Empty;
    public string BankName { get; private set; } = string.Empty;
    public string Agency { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public string AccountDigit { get; private set; } = string.Empty;
    public BankAccountType AccountType { get; private set; }
    public string Alias { get; private set; } = string.Empty;
    public decimal? OpeningBalance { get; private set; }

    /// <summary>Last reconciled (real) balance for this account — the cash-flow anchor. Null until informed.</summary>
    public decimal? ReconciledBalance { get; private set; }

    /// <summary>Date the <see cref="ReconciledBalance"/> refers to. Movements after this date are added to it.</summary>
    public DateOnly? ReconciledAt { get; private set; }

    /// <summary>CNAB 240 agreement/convenio code assigned by the bank for this account. Empty string when not configured.</summary>
    public string CnabAgreementCode { get; private set; } = string.Empty;

    /// <summary>The account identifier the bank writes in the OFX <c>BANKACCTFROM/ACCTID</c> (e.g. agency+account+digit,
    /// like Santander's <c>3578130509766</c>). Used to warn when an imported statement belongs to another account.
    /// Null when not configured — import then falls back to comparing agency/account/digit digits.</summary>
    public string? OfxAccountId { get; private set; }

    /// <summary>Loose reference (no cross-module FK) to the accounting <c>LedgerAccount</c> that represents this bank
    /// account. Used as the credit account when settling a payable (baixa); null falls back to the configured
    /// global cash code. Mirrors <c>Payable.AccountingAccountId</c> / <c>Receivable.LedgerAccountId</c>.</summary>
    public Guid? LedgerAccountId { get; private set; }

    /// <summary>Agency check digit (DV) — CNAB header pos 58 and Segmento A/J-52. Empty when not informed.</summary>
    public string AgencyDigit { get; private set; } = string.Empty;

    // Paying-company address for the CNAB Header de Lote (pos 143-222). Empty until configured.
    public string AddressStreet { get; private set; } = string.Empty;
    public string AddressNumber { get; private set; } = string.Empty;
    public string AddressComplement { get; private set; } = string.Empty;
    public string AddressCity { get; private set; } = string.Empty;
    public string AddressCep { get; private set; } = string.Empty;
    public string AddressState { get; private set; } = string.Empty;

    /// <summary>Paying CNPJ (digits only) written into the CNAB header/J-52 when this account pays under a legal
    /// identity different from its owning company. Empty falls back to the company's CNPJ at remittance time.</summary>
    public string PayerCnpj { get; private set; } = string.Empty;

    /// <summary>Paying legal name written into the CNAB header/J-52. Empty falls back to the company's legal name.</summary>
    public string PayerLegalName { get; private set; } = string.Empty;

    public bool IsDefaultForPayments { get; private set; }

    public bool Active { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private BankAccount() { }

    public BankAccount(Guid companyId, string bankCode, string bankName, string agency, string accountNumber,
        string accountDigit, BankAccountType accountType, string alias, decimal? openingBalance,
        string cnabAgreementCode = "", Guid? ledgerAccountId = null, string? ofxAccountId = null)
    {
        CompanyId = companyId;
        Apply(bankCode, bankName, agency, accountNumber, accountDigit, accountType, alias, openingBalance, cnabAgreementCode, ledgerAccountId, ofxAccountId);
    }

    public void Update(string bankCode, string bankName, string agency, string accountNumber,
        string accountDigit, BankAccountType accountType, string alias, decimal? openingBalance,
        string cnabAgreementCode = "", Guid? ledgerAccountId = null, string? ofxAccountId = null) =>
        Apply(bankCode, bankName, agency, accountNumber, accountDigit, accountType, alias, openingBalance, cnabAgreementCode, ledgerAccountId, ofxAccountId);

    private void Apply(string bankCode, string bankName, string agency, string accountNumber,
        string accountDigit, BankAccountType accountType, string alias, decimal? openingBalance,
        string cnabAgreementCode, Guid? ledgerAccountId, string? ofxAccountId)
    {
        BankCode = BankCodes.Normalize(bankCode);
        BankName = bankName.Trim();
        Agency = agency.Trim();
        AccountNumber = accountNumber.Trim();
        AccountDigit = accountDigit.Trim();
        AccountType = accountType;
        Alias = alias.Trim();
        OpeningBalance = openingBalance;
        CnabAgreementCode = string.IsNullOrWhiteSpace(cnabAgreementCode) ? "" : cnabAgreementCode.Trim();
        LedgerAccountId = ledgerAccountId;
        OfxAccountId = string.IsNullOrWhiteSpace(ofxAccountId) ? null : ofxAccountId.Trim();
    }

    /// <summary>
    /// Sets the CNAB remittance-header profile of the paying account: the agency check digit (DV) and
    /// the paying company's address written into the Header de Lote. All optional (empty = not informed).
    /// </summary>
    public void SetRemittanceProfile(string? agencyDigit, string? street, string? number,
        string? complement, string? city, string? cep, string? state)
    {
        AgencyDigit = (agencyDigit ?? string.Empty).Trim();
        AddressStreet = (street ?? string.Empty).Trim();
        AddressNumber = (number ?? string.Empty).Trim();
        AddressComplement = (complement ?? string.Empty).Trim();
        AddressCity = (city ?? string.Empty).Trim();
        AddressCep = (cep ?? string.Empty).Trim();
        AddressState = (state ?? string.Empty).Trim();
    }

    /// <summary>Sets the CNAB payer identity (CNPJ stored digits-only + legal name) of this paying account.
    /// Empty falls back to the owning company at remittance generation.</summary>
    public void SetPayerIdentity(string? cnpj, string? legalName)
    {
        PayerCnpj = DocumentValidator.Normalize(cnpj);
        PayerLegalName = (legalName ?? string.Empty).Trim();
    }

    /// <summary>Sets the reconciled-balance anchor: the real balance of the account as of a given date.</summary>
    public void SetReconciledBalance(decimal balance, DateOnly asOf)
    {
        ReconciledBalance = balance;
        ReconciledAt = asOf;
    }

    /// <summary>Marks/unmarks this account as the company's default paying account. The CNAB payment method
    /// is NOT stored here — it comes from each installment/payable (AEL-933): the account only decides WHICH
    /// account pays, not HOW.</summary>
    public void SetAsDefaultPayer() { IsDefaultForPayments = true; }
    public void ClearDefaultPayer() { IsDefaultForPayments = false; }

    public void Activate() => Active = true;
    public void Deactivate() => Active = false;
    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}

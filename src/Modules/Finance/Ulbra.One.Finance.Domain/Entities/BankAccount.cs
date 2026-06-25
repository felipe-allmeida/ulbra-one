using Ulbra.One.Common.Domain.Auditing;
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

    public bool IsDefaultForPayments { get; private set; }
    public PaymentMethod? DefaultPaymentMethod { get; private set; }

    public bool Active { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private BankAccount() { }

    public BankAccount(Guid companyId, string bankCode, string bankName, string agency, string accountNumber,
        string accountDigit, BankAccountType accountType, string alias, decimal? openingBalance,
        string cnabAgreementCode = "")
    {
        CompanyId = companyId;
        Apply(bankCode, bankName, agency, accountNumber, accountDigit, accountType, alias, openingBalance, cnabAgreementCode);
    }

    public void Update(string bankCode, string bankName, string agency, string accountNumber,
        string accountDigit, BankAccountType accountType, string alias, decimal? openingBalance,
        string cnabAgreementCode = "") =>
        Apply(bankCode, bankName, agency, accountNumber, accountDigit, accountType, alias, openingBalance, cnabAgreementCode);

    private void Apply(string bankCode, string bankName, string agency, string accountNumber,
        string accountDigit, BankAccountType accountType, string alias, decimal? openingBalance,
        string cnabAgreementCode)
    {
        BankCode = bankCode.Trim();
        BankName = bankName.Trim();
        Agency = agency.Trim();
        AccountNumber = accountNumber.Trim();
        AccountDigit = accountDigit.Trim();
        AccountType = accountType;
        Alias = alias.Trim();
        OpeningBalance = openingBalance;
        CnabAgreementCode = string.IsNullOrWhiteSpace(cnabAgreementCode) ? "" : cnabAgreementCode.Trim();
    }

    /// <summary>Sets the reconciled-balance anchor: the real balance of the account as of a given date.</summary>
    public void SetReconciledBalance(decimal balance, DateOnly asOf)
    {
        ReconciledBalance = balance;
        ReconciledAt = asOf;
    }

    /// <summary>Marks this account as the company's default paying account, with the method auto-payments use.</summary>
    public void SetAsDefaultPayer(PaymentMethod method) { IsDefaultForPayments = true; DefaultPaymentMethod = method; }
    public void ClearDefaultPayer() { IsDefaultForPayments = false; DefaultPaymentMethod = null; }

    public void Activate() => Active = true;
    public void Deactivate() => Active = false;
    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}

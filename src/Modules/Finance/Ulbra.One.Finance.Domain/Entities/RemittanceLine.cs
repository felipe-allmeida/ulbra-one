using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Finance.Domain.Remittances;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>A single payment instruction within a <see cref="Remittance"/> CNAB240 batch.</summary>
public class RemittanceLine : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RemittanceId { get; private set; }
    public Guid PayableId { get; private set; }
    public Guid InstallmentId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public string FavoredName { get; private set; } = string.Empty;
    public string FavoredDocument { get; private set; } = string.Empty;
    public string FavoredBankCode { get; private set; } = string.Empty;
    public string FavoredAgency { get; private set; } = string.Empty;
    public string FavoredAccount { get; private set; } = string.Empty;
    public string FavoredAccountDigit { get; private set; } = string.Empty;
    public string FavoredPixKey { get; private set; } = string.Empty;

    /// <summary>1-based sequence within the remittance, assigned by <see cref="Remittance.AddLine"/>.</summary>
    public int Sequence { get; private set; }

    /// <summary>Computed deterministic identifier: <c>$"{SequenceNumber:D6}{Sequence:D5}"</c> (≤ 11 chars, "Seu Número" field).</summary>
    public string Reference { get; private set; } = string.Empty;

    public RemittanceLineStatus Status { get; private set; } = RemittanceLineStatus.Pending;

    /// <summary>Effective settlement date, set when the bank confirms payment.</summary>
    public DateOnly? EffectiveDate { get; private set; }

    /// <summary>Bank-assigned reference returned in the CNAB return file (e.g. "Nosso Número").</summary>
    public string? BankReference { get; private set; }

    /// <summary>Reason code / description from the CNAB return file when the line is rejected.</summary>
    public string? ReturnReason { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private RemittanceLine() { }

    internal RemittanceLine(Guid remittanceId, RemittanceLineDraft draft, int sequence, int remittanceSequenceNumber)
    {
        RemittanceId = remittanceId;
        PayableId = draft.PayableId;
        InstallmentId = draft.InstallmentId;
        Method = draft.Method;
        Amount = draft.Amount;
        FavoredName = draft.FavoredName;
        FavoredDocument = draft.FavoredDocument;
        FavoredBankCode = draft.FavoredBankCode;
        FavoredAgency = draft.FavoredAgency;
        FavoredAccount = draft.FavoredAccount;
        FavoredAccountDigit = draft.FavoredAccountDigit;
        FavoredPixKey = draft.FavoredPixKey;
        Sequence = sequence;
        Reference = $"{remittanceSequenceNumber:D6}{sequence:D5}";
    }

    /// <summary>Transitions the line from <see cref="RemittanceLineStatus.Pending"/> to <see cref="RemittanceLineStatus.Paid"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the line is not in <see cref="RemittanceLineStatus.Pending"/> status.</exception>
    public void MarkPaid(DateOnly effectiveDate, string? bankReference)
    {
        if (Status != RemittanceLineStatus.Pending)
            throw new InvalidOperationException($"Cannot mark a {Status} line as Paid. Only Pending lines can be paid.");

        Status = RemittanceLineStatus.Paid;
        EffectiveDate = effectiveDate;
        BankReference = bankReference;
    }

    /// <summary>Transitions the line from <see cref="RemittanceLineStatus.Pending"/> to <see cref="RemittanceLineStatus.Returned"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the line is not in <see cref="RemittanceLineStatus.Pending"/> status.</exception>
    public void MarkReturned(string reason)
    {
        if (Status != RemittanceLineStatus.Pending)
            throw new InvalidOperationException($"Cannot mark a {Status} line as Returned. Only Pending lines can be returned.");

        Status = RemittanceLineStatus.Returned;
        ReturnReason = reason;
    }

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}

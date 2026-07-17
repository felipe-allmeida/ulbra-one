using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.Accounting.Application.Posting;

public sealed record AccountingPostingOptions(
    string PayablesLiabilityCode,
    string CashCode,
    IReadOnlyDictionary<string, string> WithholdingCodes);

public interface IPayablePostingService
{
    /// <summary>Books the provision (incurral). <paramref name="competenceDate"/> is the título's competence
    /// (Payable.IssueDate); null falls back to the posting date. <paramref name="costCenterId"/> is set on the
    /// expense (result) line only. <paramref name="documentRef"/> is the payable's document/NF number, stored on
    /// the entry's human-facing SourceReference. The competence is clamped to the current open period when closed.</summary>
    Task PostIncurralAsync(Guid companyId, string sourceKey, decimal amount, Guid? expenseAccountId,
        IReadOnlyList<(string TaxType, decimal Amount)> withholdings,
        DateOnly? competenceDate, Guid? costCenterId, string? documentRef, CancellationToken ct);

    /// <summary>Books the settlement (baixa) at <paramref name="paymentDate"/> (clamped to the open period).
    /// The cash credit uses the paying bank's ledger account (<paramref name="cashAccountId"/>) when it resolves
    /// to a postable account, otherwise falls back to the configured global cash code. <paramref name="documentRef"/>
    /// is the payable's document/NF number, stored on the entry's human-facing SourceReference.</summary>
    Task PostSettlementAsync(Guid companyId, string sourceKey, decimal amount, DateOnly paymentDate, Guid? cashAccountId, string? documentRef, CancellationToken ct);

    Task ReverseAsync(Guid companyId, string sourceKey, string reason, CancellationToken ct);

    /// <summary>Replays a parked pending posting from its stored plan; marks it resolved when a posting lands.
    /// Returns false when it still cannot post (missing plan / account, or the target period is still closed).</summary>
    Task<bool> ResolvePendingAsync(PendingAccountingPosting pending, CancellationToken ct);
}

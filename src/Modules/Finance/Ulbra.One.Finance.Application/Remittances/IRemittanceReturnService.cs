using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Processes CNAB240 return (.RET) files and performs per-line conciliation:
/// <list type="bullet">
///   <item>Paid lines → registers a <c>Payment</c> via <c>IPayablePaymentService</c> and transitions the installment to Paid.</item>
///   <item>Rejected lines → releases the installment back to Open (re-schedulable).</item>
///   <item>Divergent / unknown lines → leaves the line Pending for manual review.</item>
/// </list>
/// All writes are atomic: a single <c>SaveChangesAsync</c> commits the full outcome.
/// </summary>
public interface IRemittanceReturnService
{
    /// <summary>
    /// Parse, match and reconcile a CNAB240 return file.
    /// Stores the raw file, creates a <see cref="Finance.Domain.Entities.RemittanceReturn"/> history record,
    /// updates <see cref="Finance.Domain.Entities.RemittanceLine"/> statuses and registers payments — all in one UoW commit.
    /// Line matching is scoped to <paramref name="remittanceId"/> so that two paying accounts that produce
    /// the same <c>Reference</c> value cannot cross-contaminate each other's lines (multitenant safety).
    /// </summary>
    /// <param name="remittanceId">Id of the remittance whose .RET file is being uploaded (from the route).</param>
    /// <param name="file">Raw bytes of the .RET file.</param>
    /// <param name="fileName">Original file name (for storage and audit).</param>
    /// <param name="processedBy">Human-readable name/login of the operator who uploaded the file.</param>
    /// <param name="processedById">Id of the operator (for Payment.CreatedById audit).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<RemittanceReturnOutcome>> ProcessReturnAsync(
        Guid remittanceId,
        byte[] file,
        string fileName,
        string processedBy,
        Guid processedById,
        CancellationToken ct);

    /// <summary>
    /// Dry-run: parse a CNAB240 return file and classify each entry against the <b>pending</b> lines of
    /// the given paying account's remittances (scoped to the account so <c>Reference</c> values can't
    /// collide across accounts), <b>without persisting anything</b>. Powers the reconciliation screen's
    /// preview. Trailer divergences are reported (not blocking, since nothing is applied here).
    /// </summary>
    Task<Result<RemittanceReturnPreviewResponse>> PreviewCompanyReturnAsync(
        Guid payingBankAccountId, byte[] file, CancellationToken ct);

    /// <summary>
    /// Upload flow: parse + classify like the preview, then <b>auto-confirm the "Paid" lines</b> (register
    /// the payment / baixa) so the operator never has to click through titles the bank clearly settled — the
    /// exact-value guard in the classifier means only exact matches are "Paid". Divergent / rejected /
    /// unmatched lines are left untouched for manual decision. Returns the refreshed preview (auto-confirmed
    /// lines now read as <c>AlreadyProcessed</c>) with <see cref="RemittanceReturnPreviewResponse.AutoConfirmed"/>
    /// set. When the trailer diverges nothing is auto-applied (integrity gate) — it behaves as a plain preview.
    /// Either way the raw file is stored and a <see cref="Finance.Domain.Entities.RemittanceReturn"/> record is
    /// written: the bank's own document has to survive the upload, and a file that applied nothing is exactly
    /// the one the operator needs back.
    /// </summary>
    Task<Result<RemittanceReturnPreviewResponse>> AutoConfirmPaidAndPreviewAsync(
        Guid companyId, Guid payingBankAccountId, byte[] file, string fileName,
        string processedBy, Guid processedById, CancellationToken ct);

    /// <summary>
    /// Manually confirms a Pending remittance line as paid (e.g. after manual bank verification, or from
    /// the reconciliation preview). When the return's settlement data is supplied, it is recorded on the
    /// line (effective date / "Nosso Número" / authentication); otherwise today's date is used.
    /// </summary>
    Task<Result> ConfirmLinePaidAsync(
        Guid remittanceLineId, Guid processedById, CancellationToken ct,
        DateOnly? effectiveDate = null, string? bankReference = null, string? authCode = null);

    /// <summary>
    /// Manually marks a Pending remittance line as returned / rejected, releasing the installment back to Open.
    /// </summary>
    Task<Result> MarkLineReturnedAsync(Guid remittanceLineId, string reason, CancellationToken ct);

    /// <summary>
    /// Reads a CRÍTICA file (accept/reject) against the account's lines. Classifies and reports only —
    /// never registers a payment or changes status. The operator removes rejected titles from the batch
    /// via <see cref="MarkLineReturnedAsync"/>.
    /// </summary>
    Task<Result<RemittanceCriticismResponse>> PreviewCriticismAsync(
        Guid payingBankAccountId, byte[] file, CancellationToken ct);
}

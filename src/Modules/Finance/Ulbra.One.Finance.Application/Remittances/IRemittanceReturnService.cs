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
    /// Manually confirms a Pending remittance line as paid (e.g. after manual bank verification).
    /// Registers a Payment via <c>IPayablePaymentService</c> using today's date as the effective date.
    /// </summary>
    Task<Result> ConfirmLinePaidAsync(Guid remittanceLineId, Guid processedById, CancellationToken ct);

    /// <summary>
    /// Manually marks a Pending remittance line as returned / rejected, releasing the installment back to Open.
    /// </summary>
    Task<Result> MarkLineReturnedAsync(Guid remittanceLineId, string reason, CancellationToken ct);
}

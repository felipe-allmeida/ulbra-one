using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Remittances;

public interface IRemittanceGenerationService
{
    /// <summary>
    /// <paramref name="installmentIds"/> null/empty = cutoff mode (every eligible installment with
    /// ScheduledPaymentDate &lt;= <paramref name="dueCutoff"/>). Non-empty = selection mode: only the
    /// listed installments are considered, the cutoff is IGNORED (the selection is the intent), and
    /// ineligible/unknown ids come back in <see cref="GenerateRemittanceOutcome.Skipped"/>.
    /// <para>
    /// <paramref name="paymentDate"/> is the date the bank is instructed to execute the payments
    /// (CNAB <c>data_pagamento</c>), letting the operator schedule a batch ahead. Null = today.
    /// </para>
    /// </summary>
    Task<Result<GenerateRemittanceOutcome>> GenerateAsync(
        Guid companyId, Guid payingBankAccountId, DateOnly dueCutoff, string generatedBy,
        IReadOnlyList<Guid>? installmentIds, DateOnly? paymentDate, CancellationToken ct);

    Task<Result> CancelAsync(Guid remittanceId, CancellationToken ct);
}

/// <summary>Outcome of a generation run: one <see cref="Remittance"/> per payment category present in the
/// selection (AEL-933 — PIX / TED / boleto bancário / convênio), plus the installments that were skipped.</summary>
public sealed record GenerateRemittanceOutcome(IReadOnlyList<Remittance> Remittances, IReadOnlyList<SkippedInstallment> Skipped);

public sealed record SkippedInstallment(Guid PayableId, Guid InstallmentId, string Reason);

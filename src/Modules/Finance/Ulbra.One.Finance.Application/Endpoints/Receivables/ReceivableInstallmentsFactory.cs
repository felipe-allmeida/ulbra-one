using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Domain.Entities;
using CR = Ulbra.One.Finance.Application.Endpoints.Receivables.CreateReceivable.CreateReceivable;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables;

/// <summary>Builds the installment list from either a generation spec or an explicit list, applying
/// the shared validation. Centralizes logic that was duplicated between Create and Update.</summary>
internal static class ReceivableInstallmentsFactory
{
    public static Result<List<ReceivableInstallment>> Build(
        CR.ReceivableGenerateInput? generate,
        List<CR.ReceivableInstallmentInput>? installments)
    {
        if (generate is { } g)
        {
            if (g.TotalAmount <= 0 || g.Count < 1)
                return Result.Failure<List<ReceivableInstallment>>(
                    Error.Validation("Receivable.InstallmentsInvalid", "Invalid installment generation."));
            return Result.Success(Receivable.GenerateInstallments(g.TotalAmount, g.Count, g.FirstDueDate, g.IntervalMonths));
        }

        if (installments is { Count: > 0 } rows)
        {
            if (rows.Any(r => r.Amount <= 0))
                return Result.Failure<List<ReceivableInstallment>>(
                    Error.Validation("Receivable.InstallmentsInvalid", "Each installment amount must be greater than zero."));
            return Result.Success(rows.Select((r, i) => new ReceivableInstallment(i + 1, r.DueDate, r.Amount)).ToList());
        }

        return Result.Failure<List<ReceivableInstallment>>(
            Error.Validation("Receivable.InstallmentsInvalid", "Provide either explicit installments or a generation spec."));
    }
}

using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Remittances;

public interface IRemittanceGenerationService
{
    Task<Result<GenerateRemittanceOutcome>> GenerateAsync(
        Guid companyId, Guid payingBankAccountId, DateOnly dueCutoff, string generatedBy, CancellationToken ct);

    Task<Result> CancelAsync(Guid remittanceId, CancellationToken ct);
}

public sealed record GenerateRemittanceOutcome(Remittance Remittance, IReadOnlyList<SkippedInstallment> Skipped);

public sealed record SkippedInstallment(Guid PayableId, Guid InstallmentId, string Reason);

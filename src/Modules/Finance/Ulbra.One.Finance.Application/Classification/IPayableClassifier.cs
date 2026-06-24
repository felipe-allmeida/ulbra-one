using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Classification;

public sealed record PayableClassificationResult(
    Guid? AccountingAccountId, Guid? FinancialAccountId, double Confidence, string? Rationale, string Source);

public interface IPayableClassifier
{
    Task<PayableClassificationResult> ClassifyAsync(Payable payable, CancellationToken ct);
}

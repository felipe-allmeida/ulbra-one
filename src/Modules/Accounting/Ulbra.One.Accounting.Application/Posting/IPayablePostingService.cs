namespace Ulbra.One.Accounting.Application.Posting;

public sealed record AccountingPostingOptions(string PayablesLiabilityCode, string CashCode);

public interface IPayablePostingService
{
    Task PostIncurralAsync(Guid companyId, string sourceKey, decimal amount, string? expenseCode, CancellationToken ct);
    Task PostSettlementAsync(Guid companyId, string sourceKey, decimal amount, CancellationToken ct);
    Task ReverseAsync(Guid companyId, string sourceKey, string reason, CancellationToken ct);
}

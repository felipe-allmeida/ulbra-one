using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record ClassificationSuggestionResponse(
    ChartAccountId? AccountingAccountId, ChartAccountId? FinancialAccountId, PayableId? BasedOnPayableId);

using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record ClassificationSuggestionResponse(
    LedgerAccountId? AccountingAccountId, PayableId? BasedOnPayableId);

using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("An account suggested by looking at history — what this vendor's previous payables were "
    + "classified as. Cheap and deterministic, unlike the AI suggestion.")]
public sealed record ClassificationSuggestionResponse(
    [property: SwaggerSchema("The suggested account. Null when there is no history to go on.")]
    LedgerAccountId? AccountingAccountId,
    [property: SwaggerSchema("The earlier payable the suggestion was copied from — it is what lets the "
        + "accountant judge whether the precedent applies.")]
    PayableId? BasedOnPayableId);

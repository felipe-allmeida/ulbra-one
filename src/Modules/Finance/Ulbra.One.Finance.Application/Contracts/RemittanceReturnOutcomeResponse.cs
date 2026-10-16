using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Aggregate outcome of processing a CNAB240 return (.RET) file.</summary>
/// <param name="Matched">Lines in the return file that matched a known <c>RemittanceLine</c> by reference.</param>
/// <param name="Paid">Lines resolved as paid (value match) → Payment registered.</param>
/// <param name="Returned">Lines resolved as rejected by the bank → installment released back to Open.</param>
/// <param name="Unmatched">Lines whose reference did not match any known remittance line.</param>
/// <param name="Divergent">Lines marked Paid by the bank but with a value divergence or unknown outcome — left Pending for manual review.</param>
/// <param name="Notes">Human-readable notes for divergent or otherwise noteworthy entries.</param>
[SwaggerSchema("What processing a CNAB return file actually did. Unlike the preview, this reports "
    + "changes that were committed.")]
public sealed record RemittanceReturnOutcome(
    [property: SwaggerSchema("Entries matched to a known remittance line by reference.")]
    [property: SwaggerExample("125")]
    int Matched,
    [property: SwaggerSchema("Entries settled — a payment was registered for each.")]
    [property: SwaggerExample("119")]
    int Paid,
    [property: SwaggerSchema("Entries the bank rejected. Their installments went back to open, so they "
        + "return to the payment queue rather than being lost.")]
    [property: SwaggerExample("4")]
    int Returned,
    [property: SwaggerSchema("Entries whose reference matched nothing. Nothing was done with them.")]
    [property: SwaggerExample("1")]
    int Unmatched,
    [property: SwaggerSchema("Entries the bank reports as paid but for a different amount, or with an "
        + "outcome the system could not interpret. Left pending for manual review — no baixa applied.")]
    [property: SwaggerExample("2")]
    int Divergent,
    [property: SwaggerSchema("Human-readable notes about the divergent and otherwise noteworthy entries.")]
    IReadOnlyList<string> Notes);

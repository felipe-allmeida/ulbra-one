namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Aggregate outcome of processing a CNAB240 return (.RET) file.</summary>
/// <param name="Matched">Lines in the return file that matched a known <c>RemittanceLine</c> by reference.</param>
/// <param name="Paid">Lines resolved as paid (value match) → Payment registered.</param>
/// <param name="Returned">Lines resolved as rejected by the bank → installment released back to Open.</param>
/// <param name="Unmatched">Lines whose reference did not match any known remittance line.</param>
/// <param name="Divergent">Lines marked Paid by the bank but with a value divergence or unknown outcome — left Pending for manual review.</param>
/// <param name="Notes">Human-readable notes for divergent or otherwise noteworthy entries.</param>
public sealed record RemittanceReturnOutcome(
    int Matched,
    int Paid,
    int Returned,
    int Unmatched,
    int Divergent,
    IReadOnlyList<string> Notes);

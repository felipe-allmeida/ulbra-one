namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Coarse outcome derived from the occurrence codes on a CNAB240 Segmento-A return record.
/// </summary>
public enum ReturnOutcome
{
    /// <summary>The payment was settled by the bank.</summary>
    Paid,

    /// <summary>The payment was rejected by the bank.</summary>
    Rejected,

    /// <summary>The occurrence codes are not in any known set; manual review required.</summary>
    Unknown,
}

/// <summary>
/// Port (Application-layer interface) for resolving the coarse <see cref="ReturnOutcome"/> from
/// CNAB240 Segmento-A occurrence codes. Implemented by the bank-specific profile in Infrastructure.
/// </summary>
public interface IReturnOutcomeResolver
{
    /// <summary>Resolve the coarse outcome from up to 5 occurrence codes echoed in a return record.</summary>
    ReturnOutcome OutcomeFor(IReadOnlyList<string> occurrenceCodes);
}

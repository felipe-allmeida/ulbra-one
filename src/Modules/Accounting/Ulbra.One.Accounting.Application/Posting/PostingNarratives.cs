namespace Ulbra.One.Accounting.Application.Posting;

/// <summary>
/// Standard pt-BR journal-entry narratives (histórico contábil) for automatic postings.
/// <para>
/// These are <b>persisted domain data</b> shown to accountants in the Livro Diário/Razão — Brazilian
/// bookkeeping requires the histórico in Portuguese — and are deliberately <b>not</b> English error/UI
/// strings (which stay English and are localized on the frontend by their stable code). Centralized
/// here so the narratives are a single documented source of truth instead of literals scattered across
/// the posting service, and so the closed-period clamp annotation is a named, testable template rather
/// than an inline interpolation.
/// </para>
/// </summary>
internal static class PostingNarratives
{
    /// <summary>Incurral (provisão) of a payable's expense against Fornecedores a Pagar.</summary>
    public const string Incurral = "Provisão de despesa (a pagar)";

    /// <summary>Settlement (baixa) of a payable against the paying bank account.</summary>
    public const string Settlement = "Pagamento de fornecedor";

    /// <summary>
    /// Appends the closed-period clamp note to <paramref name="description"/> when an automatic
    /// posting's true competence fell in a closed period and was booked in the current open period
    /// (<paramref name="booked"/>) so the incurral stays paired with its settlement.
    /// </summary>
    public static string ClampedCompetence(string description, DateOnly original, DateOnly booked) =>
        $"{description} (competência {original:yyyy-MM} em período fechado; ajustada para {booked:yyyy-MM})";
}

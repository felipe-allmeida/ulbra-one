using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Cnab;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>How a return entry was classified against the candidate remittance lines.</summary>
public enum ReturnClassification
{
    /// <summary>Matched a Pending line and the occurrence + exact value say it settled.</summary>
    Paid,
    /// <summary>Matched a Pending line and the occurrence says the bank rejected it.</summary>
    Rejected,
    /// <summary>Matched a Pending line but value diverges or the outcome is unknown — manual review.</summary>
    Divergent,
    /// <summary>No candidate line matched.</summary>
    Unmatched,
    /// <summary>A fallback matched more than one Pending line — manual review, never guessed.</summary>
    Ambiguous,
    /// <summary>The reference matched a line already Paid/Returned (idempotent re-run of the same file).</summary>
    AlreadyProcessed,
}

/// <summary>Which key resolved the match.</summary>
public enum ReturnMatchMethod { None, Reference, Cnpj, Pix, Barcode }

/// <summary>Result of classifying one return entry against the candidate lines — no persistence.</summary>
public sealed record ReturnLineClassification(
    CnabReturnEntry Entry,
    RemittanceLine? MatchedLine,
    ReturnClassification Classification,
    ReturnMatchMethod MatchMethod,
    string? Note);

/// <summary>
/// Pure, stateless matcher/classifier for CNAB return entries. Given the parsed entries and a set of
/// candidate <see cref="RemittanceLine"/>s already loaded in memory, it decides, per entry, which line
/// it matches and whether it settled/was rejected/diverged — <b>without any I/O or mutation</b>.
///
/// Shared by the per-remittance apply (<c>RemittanceReturnService.ProcessReturnAsync</c>) and the
/// company-scoped preview, so both use exactly one matching implementation. Matching order mirrors the
/// legado: "Seu Número" (<see cref="RemittanceLine.Reference"/>) first, then favored CPF/CNPJ + value,
/// PIX key + value, and código de barras + value — each fallback only when it resolves to a single
/// Pending line (never guess across duplicates). Never matches by value alone. A <c>consumed</c> set
/// stops two entries from claiming the same line.
/// </summary>
public static class ReturnEntryClassifier
{
    public static IReadOnlyList<ReturnLineClassification> Classify(
        IReadOnlyList<CnabReturnEntry> entries,
        IReadOnlyList<RemittanceLine> candidates,
        IReturnOutcomeResolver outcomeResolver)
    {
        var results = new List<ReturnLineClassification>(entries.Count);
        var consumed = new HashSet<Guid>();

        foreach (var entry in entries)
        {
            var method = ReturnMatchMethod.Reference;

            // Primary: "Seu Número" on a still-Pending, not-yet-consumed line.
            var line = candidates.FirstOrDefault(l =>
                l.Reference == entry.Reference
                && l.Status == RemittanceLineStatus.Pending
                && !consumed.Contains(l.Id));

            if (line is null)
            {
                // Idempotent: same reference already Paid/Returned (re-run of the same .RET).
                var processed = candidates.FirstOrDefault(l =>
                    l.Reference == entry.Reference && l.Status != RemittanceLineStatus.Pending);
                if (processed is not null)
                {
                    results.Add(new(entry, processed, ReturnClassification.AlreadyProcessed, ReturnMatchMethod.Reference, null));
                    continue;
                }

                // Fallback 1 — favored CPF/CNPJ + exact settled value (bank altered/omitted "Seu Número").
                if (!string.IsNullOrWhiteSpace(entry.FavoredDocument) && entry.ActualValue > 0)
                {
                    var normDoc = NormalizeDoc(entry.FavoredDocument);
                    var docMatches = candidates.Where(l =>
                        l.Status == RemittanceLineStatus.Pending && !consumed.Contains(l.Id)
                        && l.Amount == entry.ActualValue
                        && NormalizeDoc(l.FavoredDocument) == normDoc).ToList();
                    if (docMatches.Count == 1) { line = docMatches[0]; method = ReturnMatchMethod.Cnpj; }
                    else if (docMatches.Count > 1)
                    {
                        results.Add(new(entry, null, ReturnClassification.Ambiguous, ReturnMatchMethod.Cnpj,
                            $"fallback CNPJ+valor ambíguo ({docMatches.Count} candidatos)"));
                        continue;
                    }
                }

                // Fallback 2 — favored PIX key + exact value (PIX transfer with no CNPJ echoed).
                if (line is null && !string.IsNullOrWhiteSpace(entry.FavoredPixKey) && entry.ActualValue > 0)
                {
                    var normPix = NormalizePix(entry.FavoredPixKey);
                    var pixMatches = candidates.Where(l =>
                        l.Status == RemittanceLineStatus.Pending && !consumed.Contains(l.Id)
                        && l.Amount == entry.ActualValue
                        && normPix.Length > 0 && NormalizePix(l.FavoredPixKey) == normPix).ToList();
                    if (pixMatches.Count == 1) { line = pixMatches[0]; method = ReturnMatchMethod.Pix; }
                    else if (pixMatches.Count > 1)
                    {
                        results.Add(new(entry, null, ReturnClassification.Ambiguous, ReturnMatchMethod.Pix,
                            $"fallback chave PIX+valor ambíguo ({pixMatches.Count} candidatos)"));
                        continue;
                    }
                }

                // Fallback 3 — código de barras + exact value (boleto/convênio; convert the stored linha digitável).
                if (line is null && !string.IsNullOrWhiteSpace(entry.Barcode) && entry.ActualValue > 0)
                {
                    var normBarcode = OnlyDigits(entry.Barcode);
                    var barcodeMatches = candidates.Where(l =>
                        l.Status == RemittanceLineStatus.Pending && !consumed.Contains(l.Id)
                        && l.Amount == entry.ActualValue
                        && l.BoletoDigitableLine != null
                        && normBarcode.Length > 0 && BarcodeOf(l.BoletoDigitableLine) == normBarcode).ToList();
                    if (barcodeMatches.Count == 1) { line = barcodeMatches[0]; method = ReturnMatchMethod.Barcode; }
                    else if (barcodeMatches.Count > 1)
                    {
                        results.Add(new(entry, null, ReturnClassification.Ambiguous, ReturnMatchMethod.Barcode,
                            $"fallback código de barras+valor ambíguo ({barcodeMatches.Count} candidatos)"));
                        continue;
                    }
                }

                if (line is null)
                {
                    results.Add(new(entry, null, ReturnClassification.Unmatched, ReturnMatchMethod.None, null));
                    continue;
                }
            }

            // Resolved to a Pending line — claim it so no later entry re-selects it.
            consumed.Add(line.Id);

            var outcome = outcomeResolver.OutcomeFor(entry.OccurrenceCodes);
            if (outcome == ReturnOutcome.Paid && entry.ActualValue == line.Amount)
            {
                results.Add(new(entry, line, ReturnClassification.Paid, method, null));
            }
            else if (outcome == ReturnOutcome.Rejected)
            {
                results.Add(new(entry, line, ReturnClassification.Rejected, method,
                    CnabReturnCodes.Explain(entry.OccurrenceCodes)));
            }
            else
            {
                var note = outcome == ReturnOutcome.Paid
                    ? $"divergência de valor (esperado {line.Amount:F2}, efetivado {entry.ActualValue:F2})"
                    : $"ocorrência desconhecida [{CnabReturnCodes.Explain(entry.OccurrenceCodes) ?? "sem código"}]";
                results.Add(new(entry, line, ReturnClassification.Divergent, method, note));
            }
        }

        return results;
    }

    /// <summary>Normalizes a CPF/CNPJ to digits-only without leading zeros, for tolerant comparison.</summary>
    private static string NormalizeDoc(string? document) =>
        string.IsNullOrEmpty(document) ? string.Empty
        : new string(document.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');

    /// <summary>Normalizes a PIX key: trimmed + lower-cased (email/EVP case). Empty when blank.</summary>
    private static string NormalizePix(string? key) =>
        string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim().ToLowerInvariant();

    /// <summary>Digits-only, for comparing barcodes regardless of formatting/padding.</summary>
    private static string OnlyDigits(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());

    /// <summary>Converts a stored linha digitável to its 44-digit código de barras; empty (never matches)
    /// when the linha digitável is malformed, instead of throwing.</summary>
    private static string BarcodeOf(string? digitableLine)
    {
        if (string.IsNullOrWhiteSpace(digitableLine))
            return string.Empty;
        try { return OnlyDigits(BoletoBarcode.ToBarcode(digitableLine)); }
        catch (ArgumentException) { return string.Empty; }
    }
}

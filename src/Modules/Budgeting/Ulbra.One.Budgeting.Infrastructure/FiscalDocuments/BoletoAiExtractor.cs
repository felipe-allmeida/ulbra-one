using System.Globalization;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.FiscalDocuments;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Budgeting.Infrastructure.FiscalDocuments;

/// <summary>
/// Extracts a boleto's linha digitável (+ vencimento/emissão/valor) from a PDF/image via the AI vision layer.
/// Returns an empty <see cref="ParsedBoleto"/> (never throws) when AI is off/rate-limited/fails — the form
/// opens for manual fill. Mirrors Finance's <c>AiFiscalExtractor</c>. The linha digitável is re-derived to
/// digits and its length/convênio/DV flags recomputed here from <see cref="BoletoLine"/> — never trusted from
/// the model (which can misread a digit).
/// </summary>
public sealed class BoletoAiExtractor(IAiCompletion ai) : IBoletoExtractor
{
    private const string System =
        "You read a Brazilian boleto (bank payment slip). Return ONLY the requested structure. " +
        "LinhaDigitavel: the human-readable numeric line printed at the top (the \"linha digitável\"), NOT the " +
        "barcode — 47 digits for a boleto bancário, or 48 for an arrecadação/convênio (which starts with 8). " +
        "Return it as digits only (dots and spaces may be omitted). Vencimento and DataEmissao as yyyy-MM-dd. " +
        "Valor as a number. Use null for anything absent. Confidence 0..1 reflects how sure you are of the linha digitável.";

    private static readonly ParsedBoleto Empty = new(null, false, false, false, null, null, null, 0);

    public async Task<ParsedBoleto> ExtractAsync(string fileName, byte[] bytes, CancellationToken ct)
    {
        var prompt = new AiPrompt(System, "Extract the boleto fields from the attached document.",
            [new AiAttachment(bytes, MediaTypeFor(fileName))]);

        var result = await ai.CompleteAsync<BoletoExtractionResult>(prompt, ct: ct);
        if (!result.IsSuccess)
            return Empty;

        var r = result.Value;
        var digits = BoletoLine.Digits(r.LinhaDigitavel);
        if (digits.Length == 0)
            // Nothing usable read: keep the confidence signal but apply nothing (the form stays manual).
            return Empty with { Confidence = r.Confidence };

        return new ParsedBoleto(
            DigitableLine: digits,
            IsConvenio: BoletoLine.IsConvenio(digits),
            LengthValid: BoletoLine.IsValid(digits),
            CheckDigitsOk: BoletoLine.CheckDigitsConsistent(digits),
            DueDate: ParseDate(r.Vencimento),
            IssueDate: ParseDate(r.DataEmissao),
            Amount: r.Valor,
            Confidence: r.Confidence);
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string MediaTypeFor(string f) => f.ToLowerInvariant() switch
    {
        var x when x.EndsWith(".png") => "image/png",
        var x when x.EndsWith(".jpg") => "image/jpeg",
        var x when x.EndsWith(".jpeg") => "image/jpeg",
        var x when x.EndsWith(".webp") => "image/webp",
        _ => "application/pdf",
    };
}

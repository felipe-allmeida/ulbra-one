using System.Globalization;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>Extracts fiscal fields from non-XML documents (PDF/image) via the AI layer (vision).
/// Returns an empty ParsedFiscalDocument (never throws) when AI is off/fails — the form opens for manual fill.</summary>
public sealed class AiFiscalExtractor(IAiCompletion ai)
{
    private const string System =
        "You extract structured fields from a Brazilian fiscal document (NF-e/NFS-e/boleto/fatura). " +
        "Return ONLY the requested structure. Dates as yyyy-MM-dd. Amounts as numbers. " +
        "If a field is absent, use null. Confidence 0..1 reflects how sure you are overall.";

    public async Task<ParsedFiscalDocument> ExtractAsync(string fileName, byte[] bytes, CancellationToken ct)
    {
        var format = IsImage(fileName) ? FiscalDocumentFormat.Image : FiscalDocumentFormat.Pdf;
        var mediaType = MediaTypeFor(fileName);
        var prompt = new AiPrompt(System, "Extract the fiscal fields from the attached document.",
            [new AiAttachment(bytes, mediaType)]);

        var result = await ai.CompleteAsync<FiscalExtractionResult>(prompt, ct: ct);
        if (!result.IsSuccess)
            return Empty(format);

        var r = result.Value;
        var installments = (r.Installments ?? [])
            .Select(i => (Date: ParseDate(i.DueDate), i.Amount))
            .Where(x => x.Date is not null)
            .Select(x => new ParsedInstallment(x.Date!.Value, x.Amount)).ToList();
        var fiscal = new FiscalSummary(r.Cfops ?? [], r.Ncms ?? [], null, null, null, null, r.TotalTaxes);

        return new ParsedFiscalDocument(
            format, null, r.DocumentNumber, ParseDate(r.IssueDate), r.TotalAmount,
            DocumentValidator.Normalize(r.IssuerDocument), r.IssuerName, null,
            RawExtractionJson: "{}", Installments: installments,
            ServiceDescription: r.Description, Fiscal: fiscal);
    }

    private static ParsedFiscalDocument Empty(FiscalDocumentFormat format) =>
        new(format, null, null, null, null, null, null, null, "{}", []);

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static bool IsImage(string f) =>
        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    private static string MediaTypeFor(string f) => f.ToLowerInvariant() switch
    {
        var x when x.EndsWith(".png") => "image/png",
        var x when x.EndsWith(".jpg") => "image/jpeg",
        var x when x.EndsWith(".jpeg") => "image/jpeg",
        var x when x.EndsWith(".webp") => "image/webp",
        _ => "application/pdf",
    };
}

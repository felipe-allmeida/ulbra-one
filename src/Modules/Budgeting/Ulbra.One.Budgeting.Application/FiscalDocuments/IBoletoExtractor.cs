using Ulbra.One.Budgeting.Application.Contracts;

namespace Ulbra.One.Budgeting.Application.FiscalDocuments;

/// <summary>
/// Reads a boleto's linha digitável (+ vencimento/emissão/valor) from a PDF or image via the AI vision layer.
/// Never throws — returns an empty <see cref="ParsedBoleto"/> when AI is off, rate-limited or fails, so the
/// form opens for manual fill (mirrors Finance's fiscal document extractor).
/// </summary>
public interface IBoletoExtractor
{
    Task<ParsedBoleto> ExtractAsync(string fileName, byte[] bytes, CancellationToken ct);
}

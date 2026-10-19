namespace Ulbra.One.Budgeting.Application.FiscalDocuments;

/// <summary>
/// Raw fields the vision model fills for a boleto (deserialized from the auto-generated JSON schema).
/// Mapped to <see cref="Contracts.ParsedBoleto"/> by the extractor — normalisation and the length/convênio/DV
/// flags are computed server-side from the linha digitável, never trusted from the model.
/// </summary>
public sealed record BoletoExtractionResult(
    string? LinhaDigitavel,
    string? Vencimento,
    string? DataEmissao,
    decimal? Valor,
    double Confidence);

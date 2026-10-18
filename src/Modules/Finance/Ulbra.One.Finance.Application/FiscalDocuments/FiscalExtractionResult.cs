namespace Ulbra.One.Finance.Application.FiscalDocuments;

/// <summary>Structured fields the LLM extracts from a non-XML fiscal document (PDF/image).</summary>
public sealed record FiscalExtractionResult(
    string? DocumentNumber,
    string? IssueDate,        // ISO yyyy-MM-dd; parsed to DateOnly by the extractor
    decimal? TotalAmount,
    string? IssuerDocument,   // CNPJ/CPF as printed
    string? IssuerName,
    string? Description,
    IReadOnlyList<FiscalExtractionInstallment>? Installments,
    IReadOnlyList<string>? Cfops,
    IReadOnlyList<string>? Ncms,
    decimal? TotalTaxes,
    double Confidence,
    string? Type = null);      // "Service" or "Product" (or null if undetermined) — the payable classification

public sealed record FiscalExtractionInstallment(string DueDate, decimal Amount); // DueDate ISO yyyy-MM-dd

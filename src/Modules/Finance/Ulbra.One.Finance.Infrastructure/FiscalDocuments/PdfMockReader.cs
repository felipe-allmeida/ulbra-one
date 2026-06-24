using System.Text.Json;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>
/// Placeholder reader for service invoices delivered as PDF. Real extraction (Hermes / AI agent) is a
/// future slice; for now this returns a clearly-marked mock so the UI flow can be exercised end-to-end.
/// </summary>
public sealed class PdfMockReader
{
    public ParsedFiscalDocument Read(string fileName)
    {
        var raw = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["mock"] = "true",
            ["fileName"] = fileName,
            ["note"] = "PDF reading is mocked; real extraction will use an AI agent (Hermes) in a later slice.",
        });

        return new ParsedFiscalDocument(
            FiscalDocumentFormat.PdfMock,
            AccessKey: null, DocumentNumber: null, IssueDate: null, TotalAmount: null,
            IssuerDocument: null, IssuerName: "(PDF mock — extração por IA em breve)",
            RecipientDocument: null, RawExtractionJson: raw, Installments: []);
    }
}

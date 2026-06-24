using System.Xml.Linq;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>
/// Detects an uploaded fiscal document's format and dispatches it: <c>.xml</c> is loaded and routed to
/// the first <see cref="IFiscalXmlParser"/> that recognizes its namespace; <c>.pdf</c> uses the service
/// PDF mock. Returns null for unknown extensions or unrecognized XML namespaces.
/// </summary>
public sealed class FiscalDocumentReader(IEnumerable<IFiscalXmlParser> xmlParsers, PdfMockReader pdfMock)
    : IFiscalDocumentReader
{
    public async Task<ParsedFiscalDocument?> ReadAsync(string fileName, Stream content, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName);

        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            return pdfMock.Read(fileName);

        if (!string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase))
            return null;

        if (content.CanSeek) content.Position = 0;
        var document = await XDocument.LoadAsync(content, LoadOptions.None, ct).ConfigureAwait(false);

        var parser = xmlParsers.FirstOrDefault(p => p.CanParse(document));
        return parser?.Parse(document);
    }
}

using System.Xml.Linq;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;

namespace Ulbra.One.Finance.Infrastructure.FiscalDocuments;

/// <summary>
/// Detects an uploaded fiscal document's format and dispatches it: <c>.xml</c> is loaded and routed to
/// the first <see cref="IFiscalXmlParser"/> that recognizes its namespace; <c>.pdf</c> and images (png/jpg/webp)
/// are extracted by the AI vision extractor. Returns null for unknown extensions or unrecognized XML namespaces.
/// </summary>
public sealed class FiscalDocumentReader(IEnumerable<IFiscalXmlParser> xmlParsers, AiFiscalExtractor aiExtractor)
    : IFiscalDocumentReader
{
    public async Task<ParsedFiscalDocument?> ReadAsync(string fileName, Stream content, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (ext is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            if (content.CanSeek) content.Position = 0;
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            return await aiExtractor.ExtractAsync(fileName, ms.ToArray(), ct);
        }

        if (ext != ".xml") return null;

        if (content.CanSeek) content.Position = 0;
        var document = await XDocument.LoadAsync(content, LoadOptions.None, ct).ConfigureAwait(false);

        var parser = xmlParsers.FirstOrDefault(p => p.CanParse(document));
        return parser?.Parse(document);
    }
}

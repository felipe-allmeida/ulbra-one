using Ulbra.One.Finance.Application.Contracts;

namespace Ulbra.One.Finance.Application.FiscalDocuments;

/// <summary>
/// Detects the format of an uploaded fiscal document and parses it: <c>.xml</c> is dispatched to the
/// matching <see cref="IFiscalXmlParser"/> by root namespace; <c>.pdf</c> goes to the service PDF mock.
/// </summary>
public interface IFiscalDocumentReader
{
    /// <summary>
    /// Reads and parses the file. Returns <c>null</c> when the format is unsupported (unknown extension
    /// or unrecognized XML namespace). Throws when the content is malformed.
    /// </summary>
    Task<ParsedFiscalDocument?> ReadAsync(string fileName, Stream content, CancellationToken ct);
}

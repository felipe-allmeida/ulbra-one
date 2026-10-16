using System.Xml.Linq;
using Ulbra.One.Finance.Application.Contracts;

namespace Ulbra.One.Finance.Application.FiscalDocuments;

/// <summary>
/// Parses one XML fiscal-document standard (e.g. NF-e mod. 55, NFS-e ABRASF) into a normalized
/// <see cref="ParsedFiscalDocument"/>. Detection is by the document's root namespace via
/// <see cref="CanParse"/>; the concrete XML library stays in the Infrastructure layer.
/// </summary>
public interface IFiscalXmlParser
{
    /// <summary>True when this parser recognizes the document (by root element / namespace).</summary>
    bool CanParse(XDocument document);

    /// <summary>Extracts the fiscal fields. Call only when <see cref="CanParse"/> returned true.</summary>
    ParsedFiscalDocument Parse(XDocument document);
}

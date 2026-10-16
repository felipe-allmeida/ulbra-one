using PdfSharp.Fonts;

namespace Ulbra.One.Finance.Application.Reports;

/// <summary>
/// Feeds PDFsharp an embedded DejaVu Sans (Unicode, with pt-BR glyphs) so the report renders on the Linux
/// runtime image, which ships no system fonts. Every requested family maps to DejaVu; italic is simulated
/// from the regular/bold face, so only two faces need to be embedded. Registered once at startup —
/// PDFsharp's resolver is a process-wide global, so registration is guarded to run at most once.
/// </summary>
public sealed class PdfFontResolver : IFontResolver
{
    /// <summary>Font family the report builder asks for; the resolver also maps any other family here.</summary>
    public const string FamilyName = "DejaVu Sans";

    private const string RegularFace = "DejaVuSans#";
    private const string BoldFace = "DejaVuSans#b";

    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>Registers this resolver with PDFsharp exactly once (idempotent and thread-safe — parallel
    /// integration tests share the process, so a second call must be a no-op rather than throw).</summary>
    public static void EnsureRegistered()
    {
        if (_registered) return;
        lock (Gate)
        {
            if (_registered) return;
            try
            {
                GlobalFontSettings.FontResolver = new PdfFontResolver();
            }
            catch (InvalidOperationException)
            {
                // A resolver is already registered in this process — nothing more to do.
            }
            _registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFace : RegularFace, false, isItalic);

    public byte[]? GetFont(string faceName)
    {
        var resource = faceName == BoldFace ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf";
        var assembly = typeof(PdfFontResolver).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded font resource not found: {resource}");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}

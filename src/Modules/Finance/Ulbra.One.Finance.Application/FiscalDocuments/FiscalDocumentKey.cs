namespace Ulbra.One.Finance.Application.FiscalDocuments;

/// <summary>
/// Namespacing and validation of the per-company fiscal-document storage key. The upload-url stamps
/// the <c>fiscal/{companyId:N}/</c> prefix onto the key; parse/ingest require the key to belong to the
/// authorized company's prefix, preventing reading/ingesting another company's blob via an arbitrary key
/// (IDOR) and blocking path traversal. See AEL-764.
/// </summary>
public static class FiscalDocumentKey
{
    /// <summary>Key prefix for a company's fiscal documents.</summary>
    public static string PrefixFor(Guid companyId) => $"fiscal/{companyId:N}/";

    /// <summary>Rejects malformed keys or traversal attempts (<c>..</c>, backslash, absolute path, control chars).</summary>
    public static bool IsWellFormed(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && !key.Contains("..", StringComparison.Ordinal)
        && !key.Contains('\\')
        && !key.StartsWith('/')
        && !Path.IsPathRooted(key)
        && !key.Any(char.IsControl);

    /// <summary>True when the key is well-formed and belongs to the given company's namespace.</summary>
    public static bool BelongsTo(string? key, Guid companyId) =>
        IsWellFormed(key) && key!.StartsWith(PrefixFor(companyId), StringComparison.Ordinal);
}

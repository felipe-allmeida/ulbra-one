namespace Ulbra.One.Common.Application.Storage;

/// <summary>Per-company blob key namespacing/validation under a root segment: guards against reading another
/// company's blob via an arbitrary key (IDOR) and path traversal (AEL-764). Generalises Finance's
/// <c>FiscalDocumentKey</c> so any module can reuse it — the boleto reader uses root <c>"requisition-boleto"</c>.</summary>
public static class ScopedBlobKey
{
    /// <summary>Key prefix for a company's blobs under the given root, e.g. <c>requisition-boleto/{id}/</c>.</summary>
    public static string PrefixFor(string root, Guid companyId) => $"{root}/{companyId:N}/";

    /// <summary>Rejects malformed keys or traversal attempts (<c>..</c>, backslash, absolute path, control chars).</summary>
    public static bool IsWellFormed(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && !key.Contains("..", StringComparison.Ordinal)
        && !key.Contains('\\')
        && !key.StartsWith('/')
        && !Path.IsPathRooted(key)
        && !key.Any(char.IsControl);

    /// <summary>True when the key is well-formed and belongs to the given company's namespace under <paramref name="root"/>.</summary>
    public static bool BelongsTo(string? key, string root, Guid companyId) =>
        IsWellFormed(key) && key!.StartsWith(PrefixFor(root, companyId), StringComparison.Ordinal);
}

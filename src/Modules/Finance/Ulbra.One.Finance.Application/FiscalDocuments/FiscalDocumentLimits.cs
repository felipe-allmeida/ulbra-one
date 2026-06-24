namespace Ulbra.One.Finance.Application.FiscalDocuments;

/// <summary>Shared limits for fiscal-document blobs read from storage (presigned uploads bypass the API,
/// so the size cap is enforced here, when the blob is read).</summary>
public static class FiscalDocumentLimits
{
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>Reads up to MaxBytes+1 from the stream; returns null if the blob exceeds MaxBytes.</summary>
    public static async Task<MemoryStream?> ReadCappedAsync(Stream source, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > MaxBytes) { ms.Dispose(); return null; }
            ms.Write(buffer, 0, read);
        }
        ms.Position = 0;
        return ms;
    }
}

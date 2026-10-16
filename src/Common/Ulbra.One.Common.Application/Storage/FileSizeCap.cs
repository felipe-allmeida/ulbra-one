namespace Ulbra.One.Common.Application.Storage;

/// <summary>
/// Caps the size of a blob read back from storage for parsing. Presigned uploads bypass the API, so the
/// size limit cannot be enforced on the request body — it is enforced here, when the blob is read. Shared
/// so any module reading a user-uploaded blob (e.g. the boleto reader) gets the same 25 MB ceiling.
/// </summary>
public static class FileSizeCap
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
            if (ms.Length + read > MaxBytes)
            {
                ms.Dispose();
                return null;
            }
            ms.Write(buffer, 0, read);
        }
        ms.Position = 0;
        return ms;
    }
}

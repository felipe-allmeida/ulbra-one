namespace Ulbra.One.Common.Application;

/// <summary>Result of a presigned/SAS upload URL request.</summary>
/// <param name="UploadUrl">Pre-signed URL the client should HTTP PUT the file to directly.</param>
/// <param name="Key">Storage key to persist and pass back when confirming the upload.</param>
public sealed record UploadTarget(string UploadUrl, string Key);

/// <summary>Object storage abstraction (MinIO/S3 in prod, local disk in dev). Returns/accepts a storage key.</summary>
public interface IFileStorageService
{
    Task<string> SaveAsync(string fileName, Stream content, CancellationToken ct = default);
    Task<Stream?> GetAsync(string storagePath, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Generates a short-lived URL that allows a client to upload directly to blob storage via HTTP PUT.
    /// </summary>
    /// <param name="fileName">Original file name (used to build the storage key).</param>
    /// <param name="ttl">How long the URL stays valid.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="UploadTarget"/> with the presigned URL and the storage key.</returns>
    Task<UploadTarget> CreateUploadUrl(string fileName, TimeSpan ttl, CancellationToken ct = default);
}

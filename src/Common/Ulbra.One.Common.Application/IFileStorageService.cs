using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;
namespace Ulbra.One.Common.Application;

/// <summary>Result of a presigned/SAS upload URL request.</summary>
/// <param name="UploadUrl">Pre-signed URL the client should HTTP PUT the file to directly.</param>
/// <param name="Key">Storage key to persist and pass back when confirming the upload.</param>
[SwaggerSchema("Where to upload a file directly to storage, bypassing the API. The URL is pre-signed "
    + "and short-lived, so it has to be used promptly.")]
public sealed record UploadTarget(
    [property: SwaggerSchema("Pre-signed URL to PUT the file to. It carries its own credentials — do not "
        + "attach the API's Authorization header to it.")]
    string UploadUrl,
    [property: SwaggerSchema("Storage key the file will land under. This is what gets sent back to the "
        + "API afterwards to attach the upload to a record.")]
    string Key);

/// <summary>Object storage abstraction (Azure Blob in prod, local disk in dev). Returns/accepts a storage key.</summary>
public interface IFileStorageService
{
    Task<string> SaveAsync(string fileName, Stream content, CancellationToken ct = default);

    /// <summary>Direct-write counterpart of <see cref="CreateUploadUrl"/>'s <c>keyPrefix</c> — namespaces the key. See AEL-764.</summary>
    Task<string> SaveAsync(string fileName, Stream content, string keyPrefix, CancellationToken ct = default);

    Task<Stream?> GetAsync(string storagePath, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Generates a short-lived URL that allows a client to upload directly to blob storage via HTTP PUT.
    /// </summary>
    /// <param name="fileName">Original file name (used to build the storage key).</param>
    /// <param name="ttl">How long the URL stays valid.</param>
    /// <param name="keyPrefix">Optional namespace prepended to the storage key (e.g. per-company), so a later
    /// operation can validate the key belongs to the expected scope. See AEL-764.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="UploadTarget"/> with the presigned URL and the storage key.</returns>
    Task<UploadTarget> CreateUploadUrl(string fileName, TimeSpan ttl, string? keyPrefix = null, CancellationToken ct = default);
}

namespace Ulbra.One.Common.Application;

/// <summary>Object storage abstraction (MinIO/S3 in prod, local disk in dev). Returns/accepts a storage key.</summary>
public interface IFileStorageService
{
    Task<string> SaveAsync(string fileName, Stream content, CancellationToken ct = default);
    Task<Stream?> GetAsync(string storagePath, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}

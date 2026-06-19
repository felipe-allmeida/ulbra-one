using Azure.Storage.Blobs;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class AzureBlobStorageService(BlobContainerClient container) : IFileStorageService
{
    public async Task<string> SaveAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        await container.UploadBlobAsync(key, content, ct);
        return key;
    }

    public async Task<Stream?> GetAsync(string storagePath, CancellationToken ct = default)
    {
        var blob = container.GetBlobClient(storagePath);
        if (!await blob.ExistsAsync(ct)) return null;
        var resp = await blob.DownloadStreamingAsync(cancellationToken: ct);
        return resp.Value.Content;
    }

    public async Task DeleteAsync(string storagePath, CancellationToken ct = default) =>
        await container.GetBlobClient(storagePath).DeleteIfExistsAsync(cancellationToken: ct);
}

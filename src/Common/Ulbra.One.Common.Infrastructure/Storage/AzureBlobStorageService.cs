using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class AzureBlobStorageService(BlobContainerClient container, BlobServiceClient serviceClient) : IFileStorageService
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

    public async Task<UploadTarget> CreateUploadUrl(string fileName, TimeSpan ttl, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        var blob = container.GetBlobClient(key);
        var expiry = DateTimeOffset.UtcNow.Add(ttl);
        var builder = new BlobSasBuilder(BlobSasPermissions.Write, expiry)
        {
            BlobContainerName = container.Name,
            BlobName = key,
            Resource = "b",
        };

        Uri url;
        if (blob.CanGenerateSasUri)
        {
            // Account-key connection string path (dev / Azurite)
            url = blob.GenerateSasUri(builder);
        }
        else
        {
            // Managed-identity path: user-delegation SAS via BlobServiceClient
            var udk = await serviceClient.GetUserDelegationKeyAsync(
                DateTimeOffset.UtcNow.AddMinutes(-5), expiry, ct);
            var sas = builder.ToSasQueryParameters(udk.Value, serviceClient.AccountName).ToString();
            url = new UriBuilder(blob.Uri) { Query = sas }.Uri;
        }

        return new UploadTarget(url.ToString(), key);
    }
}

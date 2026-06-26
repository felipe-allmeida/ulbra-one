using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Ulbra.One.Common.Infrastructure.Storage;

namespace Ulbra.One.UnitTests.Storage;

public sealed class CreateUploadUrlTests
{
    [Fact]
    public async Task Azure_returns_sas_put_url()
    {
        // UseDevelopmentStorage=true carries the well-known Azurite account key, so the client
        // can generate the SAS locally (no running Azurite needed).
        var serviceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        var container = serviceClient.GetBlobContainerClient("attachments");
        var sut = new AzureBlobStorageService(container, serviceClient);

        var t = await sut.CreateUploadUrl("nota.pdf", TimeSpan.FromMinutes(10));

        Assert.Contains("nota.pdf", t.Key);
        Assert.Contains("sig=", t.UploadUrl);
    }

    [Fact]
    public async Task Local_throws_not_supported()
    {
        var sut = new LocalFileStorageService(Options.Create(new ObjectStorageOptions()));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.CreateUploadUrl("x.pdf", TimeSpan.FromMinutes(1)));
    }
}

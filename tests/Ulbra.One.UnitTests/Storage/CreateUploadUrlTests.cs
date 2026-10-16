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
        var sut = new AzureBlobStorageService(container, serviceClient, Options.Create(new ObjectStorageOptions()));

        var t = await sut.CreateUploadUrl("nota.pdf", TimeSpan.FromMinutes(10));

        Assert.Contains("nota.pdf", t.Key);
        Assert.Contains("sig=", t.UploadUrl);
    }

    [Fact]
    public async Task Azure_rewrites_sas_host_to_public_endpoint()
    {
        // UseDevelopmentStorage=true → internal host is 127.0.0.1; PublicBlobEndpoint rewrites it to
        // localhost (browser-reachable in docker-compose) while keeping the signed resource path intact.
        var serviceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        var container = serviceClient.GetBlobContainerClient("attachments");
        var sut = new AzureBlobStorageService(container, serviceClient,
            Options.Create(new ObjectStorageOptions { PublicBlobEndpoint = "http://localhost:10000" }));

        var t = await sut.CreateUploadUrl("nota.pdf", TimeSpan.FromMinutes(10));
        var url = new Uri(t.UploadUrl);

        Assert.Equal("localhost", url.Host);
        Assert.Equal(10000, url.Port);
        Assert.Contains("/attachments/", url.AbsolutePath); // signed resource path preserved
        Assert.Contains("sig=", t.UploadUrl);
    }

    [Fact]
    public async Task Local_throws_not_supported()
    {
        var sut = new LocalFileStorageService(Options.Create(new ObjectStorageOptions()));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.CreateUploadUrl("x.pdf", TimeSpan.FromMinutes(1)));
    }
}

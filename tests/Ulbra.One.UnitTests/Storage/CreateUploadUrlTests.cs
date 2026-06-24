using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using Ulbra.One.Common.Infrastructure.Storage;

namespace Ulbra.One.UnitTests.Storage;

public sealed class CreateUploadUrlTests
{
    [Fact]
    public async Task S3_returns_presigned_put_url()
    {
        var s3 = new AmazonS3Client(new BasicAWSCredentials("ak", "sk"),
            new AmazonS3Config { ServiceURL = "http://localhost:9000", ForcePathStyle = true });
        var sut = new S3FileStorageService(s3, Options.Create(new ObjectStorageOptions { Bucket = "b" }));
        var t = await sut.CreateUploadUrl("nota.pdf", TimeSpan.FromMinutes(10));
        Assert.Contains("nota.pdf", t.Key);
        Assert.Contains("X-Amz-Signature", t.UploadUrl);
    }

    [Fact]
    public async Task Local_throws_not_supported()
    {
        var sut = new LocalFileStorageService(Options.Create(new ObjectStorageOptions()));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.CreateUploadUrl("x.pdf", TimeSpan.FromMinutes(1)));
    }
}

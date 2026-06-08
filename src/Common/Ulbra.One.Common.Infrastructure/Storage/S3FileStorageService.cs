using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class S3FileStorageService(IAmazonS3 s3, IOptions<ObjectStorageOptions> options) : IFileStorageService
{
    private readonly string _bucket = options.Value.Bucket;

    public async Task<string> SaveAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = GetContentType(fileName),
            AutoCloseStream = false,
            Headers = { ContentLength = content.Length },
        };
        await s3.PutObjectAsync(request, ct);
        return key;
    }

    public async Task<Stream?> GetAsync(string storagePath, CancellationToken ct = default)
    {
        try
        {
            var response = await s3.GetObjectAsync(_bucket, storagePath, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        await s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = storagePath,
        }, ct);
    }

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };
    }
}

using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class BucketInitializer(
    IAmazonS3 s3,
    IOptions<ObjectStorageOptions> options,
    ILogger<BucketInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bucket = options.Value.Bucket;

        var attempts = 0;
        const int maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(2);

        while (true)
        {
            try
            {
                var exists = await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucket);
                if (!exists)
                {
                    await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
                    logger.LogInformation("Created object storage bucket {Bucket}", bucket);
                }
                return;
            }
            catch (Exception ex) when (attempts++ < maxAttempts)
            {
                logger.LogWarning(ex, "Bucket init attempt {Attempt}/{Max} failed; retrying in {Delay}s",
                    attempts, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

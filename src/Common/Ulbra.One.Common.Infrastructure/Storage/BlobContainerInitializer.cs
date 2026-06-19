using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class BlobContainerInitializer(
    BlobContainerClient container,
    IOptions<ObjectStorageOptions> options,
    ILogger<BlobContainerInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var containerName = options.Value.Bucket;

        var attempts = 0;
        const int maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(2);

        while (true)
        {
            try
            {
                await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                logger.LogInformation("Azure Blob container '{Container}' is ready", containerName);
                return;
            }
            catch (Exception ex) when (attempts++ < maxAttempts)
            {
                logger.LogWarning(ex, "Blob container init attempt {Attempt}/{Max} failed; retrying in {Delay}s",
                    attempts, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

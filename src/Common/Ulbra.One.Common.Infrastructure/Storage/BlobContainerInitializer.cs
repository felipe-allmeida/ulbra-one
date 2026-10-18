using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class BlobContainerInitializer(
    BlobContainerClient container,
    BlobServiceClient serviceClient,
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
                await ConfigureDevCorsAsync(cancellationToken);
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

    // The fiscal upload PUTs files directly from the browser to the blob service via SAS, so the
    // storage needs CORS rules. In local dev (Azurite, account-key connection string) we set them at
    // startup. In prod the storage account's CORS is managed by Terraform and the managed identity
    // can't set service properties, so this is skipped (no ConnectionString → AccountUrl/MI path).
    private async Task ConfigureDevCorsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString))
            return;

        var props = await serviceClient.GetPropertiesAsync(ct);
        props.Value.Cors =
        [
            new BlobCorsRule
            {
                AllowedOrigins = "*",
                AllowedMethods = "GET,PUT,OPTIONS,HEAD",
                AllowedHeaders = "*",
                ExposedHeaders = "*",
                MaxAgeInSeconds = 3600,
            }
        ];
        await serviceClient.SetPropertiesAsync(props.Value, ct);
        logger.LogInformation("Local blob CORS configured for browser SAS uploads (origins=*)");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

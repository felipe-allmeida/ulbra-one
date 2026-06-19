using Amazon.Runtime;
using Amazon.S3;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Common.Infrastructure.Storage;

public static class ObjectStorageRegistration
{
    public static IServiceCollection AddObjectStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ObjectStorageOptions.SectionName);
        services.Configure<ObjectStorageOptions>(section);
        var opts = section.Get<ObjectStorageOptions>();

        var provider = opts?.Provider ?? (!string.IsNullOrWhiteSpace(opts?.Endpoint) ? StorageProvider.S3 : StorageProvider.Local);

        switch (provider)
        {
            case StorageProvider.AzureBlob:
                if (string.IsNullOrWhiteSpace(opts?.ConnectionString) && string.IsNullOrWhiteSpace(opts?.AccountUrl))
                    throw new InvalidOperationException(
                        "ObjectStorage:AccountUrl or ObjectStorage:ConnectionString is required when ObjectStorage:Provider=AzureBlob.");
                services.AddSingleton(_ =>
                {
                    var svc = !string.IsNullOrWhiteSpace(opts!.ConnectionString)
                        ? new BlobServiceClient(opts.ConnectionString)
                        : new BlobServiceClient(new Uri(opts.AccountUrl!), new DefaultAzureCredential());
                    return svc.GetBlobContainerClient(opts.Bucket);
                });
                services.AddScoped<IFileStorageService, AzureBlobStorageService>();
                services.AddHostedService<BlobContainerInitializer>();
                break;

            case StorageProvider.S3:
                if (string.IsNullOrWhiteSpace(opts?.AccessKey) || string.IsNullOrWhiteSpace(opts.SecretKey))
                    throw new InvalidOperationException("ObjectStorage:AccessKey and :SecretKey are required when :Endpoint is set.");
                services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                    new BasicAWSCredentials(opts.AccessKey, opts.SecretKey),
                    new AmazonS3Config { ServiceURL = opts!.Endpoint, ForcePathStyle = true }));
                services.AddScoped<IFileStorageService, S3FileStorageService>();
                services.AddHostedService<BucketInitializer>();
                break;

            default:
                services.AddScoped<IFileStorageService, LocalFileStorageService>();
                break;
        }

        return services;
    }
}

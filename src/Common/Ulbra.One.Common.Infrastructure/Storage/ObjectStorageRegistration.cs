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

        var provider = opts?.Provider ?? StorageProvider.Local;

        switch (provider)
        {
            case StorageProvider.AzureBlob:
                if (string.IsNullOrWhiteSpace(opts?.ConnectionString) && string.IsNullOrWhiteSpace(opts?.AccountUrl))
                    throw new InvalidOperationException(
                        "ObjectStorage:AccountUrl or ObjectStorage:ConnectionString is required when ObjectStorage:Provider=AzureBlob.");
                services.AddSingleton(_ =>
                    !string.IsNullOrWhiteSpace(opts!.ConnectionString)
                        ? new BlobServiceClient(opts.ConnectionString)
                        : new BlobServiceClient(new Uri(opts.AccountUrl!), new DefaultAzureCredential()));
                services.AddSingleton(sp =>
                    sp.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(opts!.Bucket));
                services.AddScoped<IFileStorageService, AzureBlobStorageService>();
                services.AddHostedService<BlobContainerInitializer>();
                break;

            default:
                services.AddScoped<IFileStorageService, LocalFileStorageService>();
                break;
        }

        return services;
    }
}

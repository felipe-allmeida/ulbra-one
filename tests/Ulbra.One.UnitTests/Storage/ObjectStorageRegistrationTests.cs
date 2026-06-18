using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Infrastructure.Storage;

namespace Ulbra.One.UnitTests.Storage;

public sealed class ObjectStorageRegistrationTests
{
    private static IServiceProvider BuildProvider(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var services = new ServiceCollection();
        // AddLogging is needed by hosted services (BucketInitializer, BlobContainerInitializer)
        services.AddLogging();
        services.AddObjectStorage(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void EmptyConfig_ResolvesLocalFileStorageService()
    {
        var provider = BuildProvider([]);

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        Assert.IsType<LocalFileStorageService>(service);
    }

    [Fact]
    public void S3Config_ResolvesS3FileStorageService()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ObjectStorage:Endpoint"] = "http://minio:9000",
            ["ObjectStorage:AccessKey"] = "minioadmin",
            ["ObjectStorage:SecretKey"] = "minioadmin",
        });

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        Assert.IsType<S3FileStorageService>(service);
    }

    [Fact]
    public void AzureBlobConfig_ResolvesAzureBlobStorageService()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ObjectStorage:Provider"] = "AzureBlob",
            ["ObjectStorage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["ObjectStorage:Bucket"] = "attachments",
        });

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        Assert.IsType<AzureBlobStorageService>(service);
    }
}

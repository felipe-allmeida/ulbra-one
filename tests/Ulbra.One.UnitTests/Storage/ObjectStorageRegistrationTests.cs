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
        // AddLogging is needed by the BlobContainerInitializer hosted service
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

    [Fact]
    public void AzureBlobConfig_WithAccountUrl_ResolvesAzureBlobStorageService()
    {
        // Production path: no ConnectionString, only AccountUrl → BlobServiceClient is built with
        // DefaultAzureCredential (Managed Identity). DefaultAzureCredential is lazy, so DI resolution
        // does no network I/O; this asserts the MI wiring resolves without an account key.
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ObjectStorage:Provider"] = "AzureBlob",
            ["ObjectStorage:AccountUrl"] = "https://stulbraonedev.blob.core.windows.net",
            ["ObjectStorage:Bucket"] = "attachments",
        });

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        Assert.IsType<AzureBlobStorageService>(service);
    }

    [Fact]
    public void AzureBlobConfig_MissingBothConnectionStringAndAccountUrl_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BuildProvider(new Dictionary<string, string?>
            {
                ["ObjectStorage:Provider"] = "AzureBlob",
                ["ObjectStorage:Bucket"] = "attachments",
                // neither ConnectionString nor AccountUrl supplied
            }));

        Assert.Contains("ObjectStorage:AccountUrl or ObjectStorage:ConnectionString", ex.Message);
    }
}

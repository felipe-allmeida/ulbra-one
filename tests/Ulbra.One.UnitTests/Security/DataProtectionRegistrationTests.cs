using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ulbra.One.Common.Infrastructure.Security;

namespace Ulbra.One.UnitTests.Security;

public class DataProtectionRegistrationTests
{
    private const string BlobUri = "https://acct.blob.core.windows.net/dataprotection/keys.xml";

    private static ServiceProvider Build(string? blobUri)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("DataProtection:BlobUri", blobUri)])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppDataProtection(configuration);
        return services.BuildServiceProvider();
    }

    private static string? Discriminator(ServiceProvider p) =>
        p.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;

    private static IXmlRepository? Repository(ServiceProvider p) =>
        p.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;

    [Theory]
    [InlineData(null)]
    [InlineData(BlobUri)]
    public void Pins_the_application_name_regardless_of_storage(string? blobUri)
    {
        using var provider = Build(blobUri);

        Assert.Equal("ulbra-one", Discriminator(provider));
    }

    [Fact]
    public void Persists_the_key_ring_to_blob_storage_when_configured()
    {
        using var provider = Build(BlobUri);

        var repository = Repository(provider);

        Assert.NotNull(repository);
        Assert.Contains("AzureBlob", repository.GetType().Name, StringComparison.Ordinal);
    }

    // Unconfigured must not reach for Azure: local dev and the test host stay on the filesystem.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Leaves_storage_alone_when_not_configured(string? blobUri)
    {
        using var provider = Build(blobUri);

        Assert.Null(Repository(provider));
    }
}

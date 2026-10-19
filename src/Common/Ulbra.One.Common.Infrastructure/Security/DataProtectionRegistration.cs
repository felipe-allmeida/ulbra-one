using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Common.Infrastructure.Security;

/// <summary>
/// Data Protection — the key ring behind the Google OAuth correlation cookie and antiforgery tokens.
/// Defaults keep it inside the container, so replicas disagree and sign-in fails intermittently (#227).
/// </summary>
public static class DataProtectionRegistration
{
    /// <summary>Changing this invalidates every cookie in flight — treat it as a constant.</summary>
    public const string ApplicationName = "ulbra-one";

    public const string BlobUriConfigurationKey = "DataProtection:BlobUri";

    public static IServiceCollection AddAppDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        // The discriminator is half the fix: replicas sharing one blob still derive different keys
        // if they disagree on the application name, and its default is the content root path.
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);

        // Absent by default so local dev and the test host stay on the filesystem.
        var blobUri = configuration[BlobUriConfigurationKey];
        if (!string.IsNullOrWhiteSpace(blobUri))
        {
            // Terraform owns the container; this call does not create it.
            builder.PersistKeysToAzureBlobStorage(new Uri(blobUri), new DefaultAzureCredential());
        }

        return services;
    }
}

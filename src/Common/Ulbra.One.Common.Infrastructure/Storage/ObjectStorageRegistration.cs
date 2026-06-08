using Amazon.Runtime;
using Amazon.S3;
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

        if (!string.IsNullOrWhiteSpace(opts?.Endpoint))
        {
            if (string.IsNullOrWhiteSpace(opts.AccessKey) || string.IsNullOrWhiteSpace(opts.SecretKey))
                throw new InvalidOperationException("ObjectStorage:AccessKey and :SecretKey are required when :Endpoint is set.");
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                new BasicAWSCredentials(opts.AccessKey, opts.SecretKey),
                new AmazonS3Config { ServiceURL = opts.Endpoint, ForcePathStyle = true }));
            services.AddScoped<IFileStorageService, S3FileStorageService>();
            services.AddHostedService<BucketInitializer>();
        }
        else
        {
            services.AddScoped<IFileStorageService, LocalFileStorageService>();
        }
        return services;
    }
}

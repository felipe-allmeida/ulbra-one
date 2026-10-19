using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;
using Ulbra.One.FirestoreImport.Target;

namespace Ulbra.One.FirestoreImport;

/// <summary>Registers the importer's services. Reads the target connection string from
/// <c>ConnectionStrings:Database</c> (same key/format as the app) and the source Firestore settings from
/// the <c>Firestore</c> section (defaults to the legacy project + named database).</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFirestoreImport(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database não configurada.");

        var firestore = new FirestoreOptions
        {
            ProjectId = config["Firestore:ProjectId"] ?? FirestoreOptions.DefaultProjectId,
            DatabaseId = config["Firestore:DatabaseId"] ?? FirestoreOptions.DefaultDatabaseId,
            CredentialsPath = config["Firestore:CredentialsPath"],
            CredentialsJson = config["Firestore:CredentialsJson"],
        };

        services.AddSingleton(firestore);
        services.AddSingleton<FirestoreSourceFactory>();
        services.AddSingleton(sp => sp.GetRequiredService<FirestoreSourceFactory>().Create());
        services.AddSingleton<FirestoreSource>();

        // Blob migration: legacy Firebase Storage → the One's blob container (same keys/config the app uses).
        services.AddSingleton(new BlobOptions
        {
            LegacyBucket = config["LegacyStorage:Bucket"] ?? BlobOptions.DefaultLegacyBucket,
            Container = config["ObjectStorage:Bucket"] ?? BlobOptions.DefaultContainer,
            ConnectionString = config["ObjectStorage:ConnectionString"] ?? string.Empty,
            AccountUrl = config["ObjectStorage:AccountUrl"] ?? string.Empty,
        });
        services.AddSingleton<BlobMigrator>();

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<ImportStore>();
        services.AddSingleton<VendorWriter>();
        services.AddSingleton<UserWriter>();
        services.AddSingleton<BudgetWriter>();
        services.AddSingleton<RequisitionWriter>();
        services.AddSingleton<BankAccountWriter>();
        services.AddSingleton<PayableWriter>();
        services.AddSingleton<PayableFileWriter>();
        services.AddSingleton<ProjectWriter>();
        services.AddSingleton<AdjustmentWriter>();
        services.AddSingleton<ProjectLinkWriter>();
        services.AddSingleton<ImportRunner>();
        services.AddSingleton<ImportJobRunner>();

        return services;
    }
}

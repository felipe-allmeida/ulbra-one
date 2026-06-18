namespace Ulbra.One.Common.Infrastructure.Storage;

public enum StorageProvider { Local, S3, AzureBlob }

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public string? Endpoint { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Bucket { get; set; } = "ulbra-one-attachments";
    public string LocalPath { get; set; } = "/tmp/ulbra-one-attachments";
    /// <summary>Explicit provider selection. When null, infers: S3 if Endpoint is set, else Local.</summary>
    public StorageProvider? Provider { get; set; }
    /// <summary>Azure Blob Storage account URL (used with DefaultAzureCredential when ConnectionString is absent).</summary>
    public string? AccountUrl { get; set; }
    /// <summary>Azure Blob Storage connection string (takes priority over AccountUrl; supports Azurite via UseDevelopmentStorage=true).</summary>
    public string? ConnectionString { get; set; }
}

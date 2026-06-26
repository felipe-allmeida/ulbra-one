namespace Ulbra.One.Common.Infrastructure.Storage;

public enum StorageProvider { Local, AzureBlob }

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public string Bucket { get; set; } = "ulbra-one-attachments";
    public string LocalPath { get; set; } = "/tmp/ulbra-one-attachments";
    /// <summary>Explicit provider selection. When null, defaults to Local.</summary>
    public StorageProvider? Provider { get; set; }
    /// <summary>Azure Blob Storage account URL (used with DefaultAzureCredential when ConnectionString is absent).</summary>
    public string? AccountUrl { get; set; }
    /// <summary>Azure Blob Storage connection string (takes priority over AccountUrl; supports Azurite via UseDevelopmentStorage=true).</summary>
    public string? ConnectionString { get; set; }
    /// <summary>Optional. When set, the scheme/host/port of generated SAS upload URLs is rewritten to this
    /// base. Use when the storage host the server reaches (e.g. Azurite at <c>azurite:10000</c> inside docker)
    /// is not resolvable from the browser, which must use <c>localhost:10000</c>. The SAS signature covers the
    /// canonical resource path (account/container/blob), not the host, so rewriting only the host keeps it valid.
    /// Leave empty in prod and Aspire, where the storage endpoint is already browser-reachable.</summary>
    public string? PublicBlobEndpoint { get; set; }
}

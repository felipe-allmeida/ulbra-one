using System.Net;
using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Storage.Blobs;
using Google.Apis.Auth.OAuth2;
using Ulbra.One.FirestoreImport.Source;

namespace Ulbra.One.FirestoreImport.Storage;

/// <summary>Where the legacy files live (Firebase Storage) and where they go (the One's blob container). The
/// legacy blob paths stored in Firestore (e.g. <c>fiscalDocumentUrl</c>) are object paths in the Firebase
/// default bucket, not http URLs.</summary>
public sealed record BlobOptions
{
    public const string DefaultLegacyBucket = "spherical-cell-g07pf.firebasestorage.app";
    // The One's blob container — canonical name across docker-compose and every Terraform env is "attachments"
    // (the Terraform storage module creates a container literally named "attachments"). The importer serves
    // nothing itself; it only needs to WRITE to the same container the app READS from.
    public const string DefaultContainer = "attachments";

    /// <summary>Firebase Storage bucket the legacy object paths resolve against.</summary>
    public string LegacyBucket { get; init; } = DefaultLegacyBucket;

    /// <summary>The One's blob container (mirrors <c>ObjectStorage:Bucket</c>).</summary>
    public string Container { get; init; } = DefaultContainer;

    /// <summary>The One's blob connection string (mirrors <c>ObjectStorage:ConnectionString</c>). Azurite
    /// locally = <c>UseDevelopmentStorage=true</c>. Takes priority over <see cref="AccountUrl"/> when set.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>The One's blob account URL for passwordless auth in Azure (mirrors <c>ObjectStorage:AccountUrl</c>,
    /// e.g. <c>https://acct.blob.core.windows.net</c>), used with a managed identity when no connection string is set.</summary>
    public string AccountUrl { get; init; } = string.Empty;
}

/// <summary>Streams a legacy Firebase Storage object into the One's blob container, reusing the importer's
/// Google credential for read access and Azure Blob for the write. Keys are DETERMINISTIC (derived from the
/// legacy path), so a re-run overwrites the same blob instead of orphaning copies. The One serves a file by
/// the stored key and takes its content-type/name from the DB row, so we only need the key to be stable.</summary>
public sealed class BlobMigrator
{
    private const string ReadScope = "https://www.googleapis.com/auth/devstorage.read_only";
    private readonly FirestoreOptions _google;
    private readonly BlobOptions _opts;
    private readonly BlobContainerClient _container;
    private readonly HttpClient _http = new();
    private GoogleCredential? _credential;

    public BlobMigrator(FirestoreOptions google, BlobOptions opts)
    {
        _google = google;
        _opts = opts;
        // Mirror the app's storage auth: a connection string (Azurite locally) takes priority; otherwise the
        // account URL + a managed identity (passwordless in Azure).
        _container = string.IsNullOrWhiteSpace(opts.ConnectionString)
            ? new BlobContainerClient(new Uri($"{opts.AccountUrl.TrimEnd('/')}/{opts.Container}"), new DefaultAzureCredential())
            : new BlobContainerClient(opts.ConnectionString, opts.Container);
    }

    /// <summary>Ensures the One container exists (Azurite starts empty). Call once per run before migrating.</summary>
    public Task EnsureContainerAsync(CancellationToken ct) => _container.CreateIfNotExistsAsync(cancellationToken: ct);

    /// <summary>Downloads <paramref name="legacyPath"/> from Firebase Storage and uploads it under
    /// <paramref name="keyPrefix"/> in the One container. Returns the stored key + size, or null when the
    /// legacy object does not exist (missing files are skipped, not fatal).</summary>
    public async Task<StoredBlob?> MigrateAsync(string legacyPath, string fileName, string keyPrefix, CancellationToken ct)
    {
        await using var source = await DownloadAsync(legacyPath, ct);
        if (source is null) return null;

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var key = $"{keyPrefix}mig_{ShortHash(legacyPath)}_{Sanitize(fileName)}";
        await _container.GetBlobClient(key).UploadAsync(buffer, overwrite: true, ct);
        return new StoredBlob(key, buffer.Length);
    }

    private async Task<Stream?> DownloadAsync(string legacyPath, CancellationToken ct)
    {
        var token = await AccessTokenAsync();
        var url = $"https://storage.googleapis.com/storage/v1/b/{_opts.LegacyBucket}/o/{Uri.EscapeDataString(legacyPath)}?alt=media";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", token);
        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) { resp.Dispose(); return null; }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    private async Task<string> AccessTokenAsync()
    {
        _credential ??= (string.IsNullOrWhiteSpace(_google.CredentialsJson)
            ? string.IsNullOrWhiteSpace(_google.CredentialsPath)
                ? await GoogleCredential.GetApplicationDefaultAsync()
                : GoogleCredential.FromFile(_google.CredentialsPath)
            : GoogleCredential.FromJson(_google.CredentialsJson)).CreateScoped(ReadScope);
        return await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync();
    }

    private static string ShortHash(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()[..16];

    // A blob-name-safe last segment; the original file name is preserved in the DB row for the download.
    private static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        var cleaned = sb.ToString().Trim('_');
        return cleaned.Length == 0 ? "file" : cleaned.Length <= 80 ? cleaned : cleaned[^80..];
    }
}

/// <summary>A migrated blob: its stored key (goes into <c>storage_path</c>) and byte size.</summary>
public readonly record struct StoredBlob(string Key, long Size);

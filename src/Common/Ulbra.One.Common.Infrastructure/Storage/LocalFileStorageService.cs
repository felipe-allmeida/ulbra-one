using Microsoft.Extensions.Options;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class LocalFileStorageService(IOptions<ObjectStorageOptions> options) : IFileStorageService
{
    private readonly string _root = options.Value.LocalPath;

    public Task<string> SaveAsync(string fileName, Stream content, CancellationToken ct = default) =>
        SaveAsync(fileName, content, keyPrefix: "", ct);

    public async Task<string> SaveAsync(string fileName, Stream content, string keyPrefix, CancellationToken ct = default)
    {
        var key = $"{keyPrefix}{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        var full = Path.Combine(_root, key);
        // The prefix may introduce sub-directories; create them.
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task<Stream?> GetAsync(string storagePath, CancellationToken ct = default)
    {
        if (!TryResolveInsideRoot(storagePath, out var full)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(File.Exists(full) ? File.OpenRead(full) : null);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        if (TryResolveInsideRoot(storagePath, out var full) && File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    public Task<UploadTarget> CreateUploadUrl(string fileName, TimeSpan ttl, string? keyPrefix = null, CancellationToken ct = default) =>
        throw new NotSupportedException("Presigned upload is not supported by the local file provider.");

    /// <summary>
    /// Defense-in-depth against path traversal: resolves the storage key under the root and rejects any
    /// key that escapes it (e.g. <c>..\..\appsettings.json</c>). The app layer also validates keys, but this
    /// guard keeps a forgotten check from turning into an arbitrary-file read/delete. See AEL-764.
    /// </summary>
    private bool TryResolveInsideRoot(string storagePath, out string fullPath)
    {
        var rootFull = Path.GetFullPath(_root);
        fullPath = Path.GetFullPath(Path.Combine(rootFull, storagePath));
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal);
    }
}

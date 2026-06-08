using Microsoft.Extensions.Options;
using Ulbra.One.Common.Application;

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class LocalFileStorageService(IOptions<ObjectStorageOptions> options) : IFileStorageService
{
    private readonly string _root = options.Value.LocalPath;

    public async Task<string> SaveAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        var key = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        var full = Path.Combine(_root, key);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task<Stream?> GetAsync(string storagePath, CancellationToken ct = default)
    {
        var full = Path.Combine(_root, storagePath);
        return Task.FromResult<Stream?>(File.Exists(full) ? File.OpenRead(full) : null);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var full = Path.Combine(_root, storagePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }
}

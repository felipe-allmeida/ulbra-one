using Microsoft.Extensions.Options;
using Ulbra.One.Common.Infrastructure.Storage;

namespace Ulbra.One.UnitTests.Storage;

public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"ulbra-one-test-{Guid.NewGuid():N}");

    private LocalFileStorageService CreateSut() =>
        new(Options.Create(new ObjectStorageOptions { LocalPath = _tempDir }));

    [Fact]
    public async Task RoundTrip_SaveGetDelete_ReturnsCorrectBytes()
    {
        // Arrange
        var sut = CreateSut();
        var bytes = "hello storage"u8.ToArray();
        using var input = new MemoryStream(bytes);

        // Act: Save
        var key = await sut.SaveAsync("test.txt", input);

        // Assert: key is non-empty
        Assert.False(string.IsNullOrWhiteSpace(key));

        // Act: Get (scoped so the read handle is released before Delete)
        {
            await using var stream = await sut.GetAsync(key);

            // Assert: retrieved content matches
            Assert.NotNull(stream);
            using var ms = new MemoryStream();
            await stream!.CopyToAsync(ms);
            Assert.Equal(bytes, ms.ToArray());
        }

        // Act: Delete
        await sut.DeleteAsync(key);

        // Assert: Get after delete returns null
        var afterDelete = await sut.GetAsync(key);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task SaveAsync_WithKeyPrefix_NamespacesKey_And_RoundTrips()
    {
        // Key carries the prefix and the blob is readable back from the sub-directory it introduces.
        var sut = CreateSut();
        var bytes = "scoped content"u8.ToArray();
        using var input = new MemoryStream(bytes);

        var key = await sut.SaveAsync("nf.xml", input, keyPrefix: "fiscal/abc123/");

        Assert.StartsWith("fiscal/abc123/", key);

        await using var stream = await sut.GetAsync(key);
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task GetAsync_NonExistentKey_ReturnsNull()
    {
        var sut = CreateSut();
        var result = await sut.GetAsync("does-not-exist.txt");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentKey_DoesNotThrow()
    {
        var sut = CreateSut();
        await sut.DeleteAsync("does-not-exist.txt");
    }

    [Fact]
    public async Task GetAsync_TraversalKey_ReturnsNull_EvenIfTargetExists()
    {
        // Defense-in-depth (AEL-764): a key escaping the root must not read arbitrary files.
        var sut = CreateSut();
        var parent = Path.GetDirectoryName(_tempDir.TrimEnd(Path.DirectorySeparatorChar))!;
        var secretName = $"secret-{Guid.NewGuid():N}.txt";
        var outside = Path.Combine(parent, secretName);
        await File.WriteAllTextAsync(outside, "top secret");
        try
        {
            var stream = await sut.GetAsync($"../{secretName}");
            Assert.Null(stream);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task DeleteAsync_TraversalKey_DoesNotDeleteOutsideRoot()
    {
        var sut = CreateSut();
        var parent = Path.GetDirectoryName(_tempDir.TrimEnd(Path.DirectorySeparatorChar))!;
        var secretName = $"secret-{Guid.NewGuid():N}.txt";
        var outside = Path.Combine(parent, secretName);
        await File.WriteAllTextAsync(outside, "top secret");
        try
        {
            await sut.DeleteAsync($"../{secretName}");
            Assert.True(File.Exists(outside)); // the guard prevented the escaping delete
        }
        finally
        {
            File.Delete(outside);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

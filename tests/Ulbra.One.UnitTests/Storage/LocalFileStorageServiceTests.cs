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

        // Act: Get
        var stream = await sut.GetAsync(key);

        // Assert: retrieved content matches
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());

        // Act: Delete
        await sut.DeleteAsync(key);

        // Assert: Get after delete returns null
        var afterDelete = await sut.GetAsync(key);
        Assert.Null(afterDelete);
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

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

namespace Ulbra.One.Common.Infrastructure.Storage;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public string? Endpoint { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Bucket { get; set; } = "ulbra-one-attachments";
    public string LocalPath { get; set; } = "/tmp/ulbra-one-attachments";
}

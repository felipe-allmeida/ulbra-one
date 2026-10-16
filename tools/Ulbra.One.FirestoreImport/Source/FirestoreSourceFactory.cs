using Google.Cloud.Firestore;

namespace Ulbra.One.FirestoreImport.Source;

/// <summary>Connection settings for the legacy orçamento Firestore. The source lives in a NAMED
/// database (not "(default)"), so <see cref="DatabaseId"/> must be passed to the builder.</summary>
public sealed record FirestoreOptions
{
    public const string DefaultProjectId = "spherical-cell-g07pf";
    public const string DefaultDatabaseId = "ai-studio-9c0ba35e-15c2-4fa9-96ad-eb738fcf5bb5";

    public string ProjectId { get; init; } = DefaultProjectId;
    public string DatabaseId { get; init; } = DefaultDatabaseId;

    /// <summary>Optional path to a GCP service-account key JSON file. When null, Application Default
    /// Credentials are used (local dev via gcloud ADC).</summary>
    public string? CredentialsPath { get; init; }

    /// <summary>Optional GCP service-account key as raw JSON. Preferred in Azure, where the key comes
    /// from Key Vault as a secret env var (no file to mount). Takes precedence over
    /// <see cref="CredentialsPath"/>; when both are null, ADC is used.</summary>
    public string? CredentialsJson { get; init; }
}

/// <summary>Builds a <see cref="FirestoreDb"/> bound to the legacy project's named database.</summary>
public sealed class FirestoreSourceFactory(FirestoreOptions options)
{
    public FirestoreDb Create()
    {
        var builder = new FirestoreDbBuilder
        {
            ProjectId = options.ProjectId,
            DatabaseId = options.DatabaseId,
        };

        if (!string.IsNullOrWhiteSpace(options.CredentialsJson))
            builder.JsonCredentials = options.CredentialsJson;
        else if (!string.IsNullOrWhiteSpace(options.CredentialsPath))
            builder.CredentialsPath = options.CredentialsPath;

        return builder.Build();
    }
}

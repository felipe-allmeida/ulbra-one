namespace Ulbra.One.Common.Infrastructure.Ai;

public enum AiProvider { Fake, Anthropic, OpenAI, AzureOpenAI }

/// <summary>Config for the AI layer. Mirrors ObjectStorageOptions. ApiKey comes from Key Vault in prod.</summary>
public sealed class AiProviderOptions
{
    public const string SectionName = "Ai";

    /// <summary>Selects the implementation. Default Fake (offline; degrades to heuristic).</summary>
    public AiProvider Provider { get; set; } = AiProvider.Fake;

    /// <summary>Default model id passed to the provider.</summary>
    public string Model { get; set; } = "claude-sonnet-4-6";

    /// <summary>Provider API key. NEVER in appsettings — supplied via Key Vault (Ai--ApiKey) or env.</summary>
    public string? ApiKey { get; set; }
}

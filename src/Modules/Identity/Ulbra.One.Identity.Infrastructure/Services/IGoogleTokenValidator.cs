namespace Ulbra.One.Identity.Infrastructure.Services;

public record GoogleTokenPayload(
    string Subject,
    string Email,
    bool EmailVerified,
    string Name,
    string? HostedDomain);

public interface IGoogleTokenValidator
{
    Task<GoogleTokenPayload> ValidateAsync(string idToken, string expectedAudience, CancellationToken ct);
}

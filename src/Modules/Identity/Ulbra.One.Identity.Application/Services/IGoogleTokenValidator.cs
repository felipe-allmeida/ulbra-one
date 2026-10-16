namespace Ulbra.One.Identity.Application.Services;

public record GoogleTokenPayload(
    string Subject,
    string Email,
    bool EmailVerified,
    string Name,
    string? Picture,
    string? HostedDomain);

public interface IGoogleTokenValidator
{
    Task<GoogleTokenPayload> ValidateAsync(string idToken, string expectedAudience, CancellationToken ct);
}

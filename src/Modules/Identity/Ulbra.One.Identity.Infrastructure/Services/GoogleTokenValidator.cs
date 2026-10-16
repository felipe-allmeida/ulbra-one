using Ulbra.One.Identity.Application.Services;
using Google.Apis.Auth;

namespace Ulbra.One.Identity.Infrastructure.Services;

internal sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    public async Task<GoogleTokenPayload> ValidateAsync(string idToken, string expectedAudience, CancellationToken ct)
    {
        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = [expectedAudience]
        };

        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

        return new GoogleTokenPayload(
            Subject: payload.Subject,
            Email: payload.Email,
            EmailVerified: payload.EmailVerified,
            Name: payload.Name ?? string.Empty,
            Picture: payload.Picture,
            HostedDomain: payload.HostedDomain);
    }
}

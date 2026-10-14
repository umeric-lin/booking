using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Booking.Api.Auth;

/// <summary>
/// Mints signed bearer tokens.
/// </summary>
/// <remarks>
/// This stands in for a real identity provider. In production the signing key would live in a
/// secret store, tokens would be issued elsewhere, and this type would not exist.
/// </remarks>
public sealed class TokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    public (string AccessToken, DateTimeOffset ExpiresAt) Issue(string subject)
    {
        var settings = options.Value;
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(settings.TokenLifetimeMinutes);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
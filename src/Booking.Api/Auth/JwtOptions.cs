using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Booking.Api.Auth;

/// <summary>
/// Bearer token settings, bound from the <c>Jwt</c> configuration section.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 signing secret. Symmetric, so a single deployment verifies its own tokens.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Stand-in for a real identity provider's client credentials. Whoever presents this may mint
    /// a token. Only meaningful while this is a single-deployment demo.
    /// </summary>
    public string DevApiKey { get; set; } = string.Empty;

    public SymmetricSecurityKey CreateSigningKey()
        => new(Encoding.UTF8.GetBytes(SigningKey));

    public bool MatchesApiKey(string? candidate)
    {
        if (string.IsNullOrEmpty(DevApiKey) || candidate is null)
        {
            return false;
        }

        // Hash both sides first so they are always the same length. FixedTimeEquals requires that
        // and throws otherwise, which would turn a wrong-length key into a 500 — and would leak
        // the expected length through the response time.
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(candidate)),
            SHA256.HashData(Encoding.UTF8.GetBytes(DevApiKey)));
    }
}

/// <summary>
/// Body of <c>POST /auth/token</c>.
/// </summary>
/// <remarks>
/// The attribute sits on the primary-constructor parameter, not on the property: MVC throws at
/// model-binding time if a positional record carries validation metadata on its generated
/// property.
/// </remarks>
public sealed record TokenRequest([Required] string? ApiKey);
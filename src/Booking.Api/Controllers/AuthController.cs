using Booking.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

/// <summary>
/// Exchanges the deployment's API key for a short-lived bearer token.
/// </summary>
/// <remarks>
/// A stand-in for a real identity provider, so that the API can be exercised end to end without
/// one. Everything else about the pipeline — signing, validation, the 401 challenge — is the part
/// that would remain.
/// </remarks>
[ApiController]
[Route("auth")]
[Tags("Auth")]
[AllowAnonymous]
public sealed class AuthController(TokenIssuer tokens) : ControllerBase
{
    /// <summary>Issues a bearer token in exchange for the deployment API key.</summary>
    /// <response code="200">A signed token and its expiry.</response>
    /// <response code="400">No API key was supplied.</response>
    /// <response code="401">The API key is wrong.</response>
    [HttpPost("token")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> CreateToken(
        [FromBody] TokenRequest request,
        [FromServices] Microsoft.Extensions.Options.IOptions<JwtOptions> options,
        [FromServices] TimeProvider timeProvider)
    {
        if (!options.Value.MatchesApiKey(request.ApiKey))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid API key.",
                Status = StatusCodes.Status401Unauthorized,
            });
        }

        // A real identity provider would put something meaningful here. Using the clock is enough
        // to show what a subject claim carries.
        var (accessToken, expiresAt) = tokens.Issue($"api-user-{timeProvider.GetUtcNow():yyyyMMdd}");

        return Ok(new TokenResponse(accessToken, "Bearer", (int)(expiresAt - timeProvider.GetUtcNow()).TotalSeconds));
    }
}

/// <summary>An issued bearer token.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
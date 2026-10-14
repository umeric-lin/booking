using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Booking.Api.Contracts;
using Xunit;

namespace Booking.Tests;

/// <summary>
/// Covers the authentication boundary: what is public, what demands a token, and what happens to
/// a bad one.
/// </summary>
public sealed class AuthenticationTests : IAsyncLifetime, IDisposable
{
    private const string TenAm = "2026-01-05T10:00:00Z";

    private readonly BookingApiFactory _factory = new();
    private HttpClient _anonymous = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    public AuthenticationTests() => _anonymous = _factory.CreateClient();

    public void Dispose()
    {
        _anonymous?.Dispose();
        _factory.Dispose();
    }

    private async Task<HttpResponseMessage> PostBookingAsync(int roomId, string title = "Standup")
        => await _anonymous.PostAsJsonAsync(
            $"/api/rooms/{roomId}/reservations",
            new { start = TenAm, end = "2026-01-05T11:00:00Z", title });

    [Fact]
    public async Task Token_endpoint_rejects_a_wrong_api_key()
    {
        var response = await _anonymous.PostAsJsonAsync("/auth/token", new { apiKey = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_endpoint_issues_a_token_for_the_right_api_key()
    {
        var token = await TestAuth.GetTokenAsync(_anonymous);

        Assert.NotEmpty(token);
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public async Task Booking_without_a_token_is_rejected()
    {
        var response = await PostBookingAsync(1);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Booking_with_a_valid_token_succeeds()
    {
        using var client = await TestAuth.CreateAuthenticatedClientAsync(_factory);

        var response = await client.PostAsJsonAsync(
            "/api/rooms/1/reservations",
            new { start = TenAm, end = "2026-01-05T11:00:00Z", title = "Standup" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_token_is_rejected()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.PostAsJsonAsync(
            "/api/rooms/1/reservations",
            new { start = TenAm, end = "2026-01-05T11:00:00Z", title = "Standup" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        // Same issuer, audience and lifetime, but signed with a key this deployment does not
        // trust — the failure mode of a leaked or rotated signing key.
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenSignedWithWrongKey());

        var response = await client.PostAsJsonAsync(
            "/api/rooms/1/reservations",
            new { start = TenAm, end = "2026-01-05T11:00:00Z", title = "Standup" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listing_rooms_stays_public()
    {
        var response = await _anonymous.GetAsync("/api/rooms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Listing_a_room_stays_public()
    {
        var response = await _anonymous.GetAsync("/api/rooms/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Listing_reservations_requires_a_token()
    {
        var response = await _anonymous.GetAsync("/api/rooms/1/reservations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_reservation_requires_a_token()
    {
        var response = await _anonymous.DeleteAsync("/api/reservations/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_remains_reachable_without_a_token()
    {
        var response = await _anonymous.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_document_advertises_a_bearer_security_scheme()
    {
        var document = await _anonymous.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("\"bearer\"", document);
        Assert.Contains("\"scheme\":\"bearer\"", document.Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_document_requires_that_scheme_rather_than_declaring_it_optional()
    {
        // Regression: the requirement once serialised as an empty object, `security: [{}]`. In
        // OpenAPI an empty requirement means "authentication is optional", and Swagger UI then
        // silently omits the Authorization header on every request. Asserting only that the
        // scheme is *defined* missed this; what matters is that it is *required*.
        using var json = JsonDocument.Parse(await _anonymous.GetStringAsync("/swagger/v1/swagger.json"));

        var security = json.RootElement.GetProperty("security");
        var requirement = Assert.Single(security.EnumerateArray().ToList());

        Assert.True(requirement.TryGetProperty("bearer", out _),
            "Root security must require the bearer scheme, not an empty requirement object.");
    }

    private static string TokenSignedWithWrongKey()
    {
        var settings = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes("a-completely-different-signing-key-0000000000"));

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "RoomBookingApi",
            audience: "RoomBookingClients",
            claims: [new System.Security.Claims.Claim("sub", "attacker")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                settings, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Booking.Api.Controllers;

namespace Booking.Tests;

/// <summary>
/// Obtains a bearer token from <c>POST /auth/token</c> for tests that exercise protected endpoints.
/// </summary>
/// <remarks>
/// Tokens are minted through the real endpoint rather than forged locally, so these tests cover
/// issuance and validation together and cannot drift from what the API actually accepts.
/// </remarks>
internal static class TestAuth
{
    public const string ApiKey = "local-dev-key";

    public static async Task<HttpClient> CreateAuthenticatedClientAsync(BookingApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(client));

        return client;
    }

    public static async Task<string> GetTokenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/token", new { apiKey = ApiKey });
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();

        return token!.AccessToken;
    }
}
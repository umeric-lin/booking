using System.Net;
using System.Text.Json;
using Xunit;

namespace Booking.Tests;

/// <summary>
/// Guards the OpenAPI document, since the UI is only useful if the document behind it stays
/// accurate — and a dropped <c>Map*</c> or a missing <c>WithSummary</c> would otherwise go
/// unnoticed.
/// </summary>
public sealed class OpenApiDocumentTests : IDisposable
{
    private readonly BookingApiFactory _factory = new();
    private readonly HttpClient _client;

    public OpenApiDocumentTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Serves_a_document_describing_every_endpoint()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/api/rooms",
                     "/api/rooms/{roomId}",
                     "/api/rooms/{roomId}/reservations",
                     "/api/reservations/{reservationId}",
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"The document is missing {path}.");
        }

        var roomReservations = paths
            .GetProperty("/api/rooms/{roomId}/reservations")
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name);

        Assert.Equal(["get", "post"], roomReservations);
    }

    [Fact]
    public async Task Describes_the_request_and_response_contracts()
    {
        using var document = JsonDocument.Parse(
            await _client.GetStringAsync("/swagger/v1/swagger.json"));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var schema in new[]
                 {
                     "RoomResponse",
                     "ReservationResponse",
                     "CreateReservationRequest",
                     "UpdateReservationRequest",
                     "ProblemDetails",
                 })
        {
            Assert.True(schemas.TryGetProperty(schema, out _), $"The document is missing {schema}.");
        }
    }

    [Fact]
    public async Task Documents_the_conflict_and_validation_status_codes()
    {
        using var document = JsonDocument.Parse(
            await _client.GetStringAsync("/swagger/v1/swagger.json"));

        var create = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/rooms/{roomId}/reservations")
            .GetProperty("post")
            .GetProperty("responses");

        Assert.True(create.TryGetProperty("201", out _));
        Assert.True(create.TryGetProperty("400", out _));
        Assert.True(create.TryGetProperty("404", out _));
        Assert.True(create.TryGetProperty("409", out _), "A booking conflict must be documented.");
    }

    [Fact]
    public async Task Redirects_the_root_to_the_ui()
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger", response.Headers.Location?.ToString());
    }
}
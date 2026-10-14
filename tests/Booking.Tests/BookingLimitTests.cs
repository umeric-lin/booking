using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace Booking.Tests;

/// <summary>
/// The two duration limits: a booking may not span more than a day, and an availability query may
/// not look further ahead than a week.
/// </summary>
/// <remarks>
/// Both boundaries are checked exactly at the limit as well as past it. "Must not exceed a day"
/// means 24 hours is allowed and 24 hours and one second is not; a test that only probes the
/// obviously-invalid value would not catch an off-by-one in the comparison.
/// </remarks>
public sealed class BookingLimitTests : IAsyncLifetime, IDisposable
{
    private const string TenAm = "2026-01-05T10:00:00Z";

    private readonly BookingApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync() => _client = await TestAuth.CreateAuthenticatedClientAsync(_factory);

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _client?.Dispose();
        _factory.Dispose();
    }

    private Task<HttpResponseMessage> BookAsync(string start, string end)
        => _client.PostAsJsonAsync(
            "/api/rooms/1/reservations",
            new { start, end, title = "Long booking" });

    [Fact]
    public async Task A_booking_of_exactly_one_day_is_allowed()
    {
        var response = await BookAsync(TenAm, "2026-01-06T10:00:00Z");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_booking_longer_than_one_day_is_rejected()
    {
        var response = await BookAsync(TenAm, "2026-01-06T10:00:01Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal("Booking rule violated", problem!.Title);
        Assert.Contains("end", problem.Errors.Keys);
        Assert.Contains("24 hours", problem.Errors["end"].Single());
    }

    [Fact]
    public async Task Rescheduling_cannot_extend_a_booking_beyond_one_day()
    {
        var created = await (await BookAsync(TenAm, "2026-01-05T11:00:00Z"))
            .Content.ReadFromJsonAsync<Booking.Api.Contracts.ReservationResponse>();

        var response = await _client.PutAsJsonAsync(
            $"/api/reservations/{created!.Id}",
            new { start = TenAm, end = "2026-01-07T10:00:00Z", title = "Too long" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_availability_window_of_exactly_one_week_is_allowed()
    {
        var response = await _client.GetAsync(
            "/api/rooms?start=2026-01-05T00:00:00Z&end=2026-01-12T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_availability_window_longer_than_one_week_is_rejected()
    {
        var response = await _client.GetAsync(
            "/api/rooms?start=2026-01-05T00:00:00Z&end=2026-01-12T00:00:01Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("end", problem!.Errors.Keys);
        Assert.Contains("7 days", problem.Errors["end"].Single());
    }

    [Fact]
    public async Task Listing_a_rooms_reservations_is_unaffected_when_no_window_is_given()
    {
        // No window means no limit to apply.
        var response = await _client.GetAsync("/api/rooms/1/reservations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_reservation_listing_window_of_exactly_one_week_is_allowed()
    {
        var response = await _client.GetAsync(
            "/api/rooms/1/reservations?start=2026-01-05T00:00:00Z&end=2026-01-12T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_reservation_listing_window_longer_than_one_week_is_rejected()
    {
        var response = await _client.GetAsync(
            "/api/rooms/1/reservations?start=2026-01-05T00:00:00Z&end=2026-01-12T00:00:01Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("end", problem!.Errors.Keys);
        Assert.Contains("7 days", problem.Errors["end"].Single());
    }

    [Fact]
    public async Task The_listing_limit_is_checked_before_the_room_is_looked_up()
    {
        // Same deliberate ordering as the reservation limit: a request that breaks both the window
        // rule and the room lookup is reported as the rule breach it is.
        var response = await _client.GetAsync(
            "/api/rooms/9999/reservations?start=2026-01-05T00:00:00Z&end=2026-03-05T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Listing_all_rooms_is_unaffected_by_the_window_limit()
    {
        // No window means no limit to apply — the rule is about how far ahead you may look.
        var response = await _client.GetAsync("/api/rooms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_limit_is_checked_before_the_database_is_touched()
    {
        // Booking into a room that does not exist would normally be a 404. Because the duration
        // rule is evaluated first, a request that breaks both is rejected as a bad request — the
        // order is deliberate, and this pins it.
        var response = await _client.PostAsJsonAsync(
            "/api/rooms/9999/reservations",
            new { start = TenAm, end = "2026-01-20T10:00:00Z", title = "Both wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
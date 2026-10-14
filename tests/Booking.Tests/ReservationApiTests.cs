using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Booking.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Booking.Tests;

/// <summary>
/// Exercises the reservation rules the brief calls out by name, over the real HTTP pipeline.
/// </summary>
/// <remarks>
/// xUnit constructs one instance per test, and each instance brings up its own application and
/// its own database. A shared fixture would mean one test's bookings were visible to the next.
/// </remarks>
public sealed class ReservationApiTests : IAsyncLifetime, IDisposable
{
    private const string TenAm = "2026-01-05T10:00:00Z";

    private readonly BookingApiFactory _factory = new();
    private HttpClient _client = null!;

    public ReservationApiTests()
    {
    }

    public async Task InitializeAsync() => _client = await TestAuth.CreateAuthenticatedClientAsync(_factory);

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _client?.Dispose();
        _factory.Dispose();
    }

    private async Task<int> GetAuditoriumIdAsync()
    {
        var rooms = await _client.GetFromJsonAsync<List<RoomResponse>>("/api/rooms");
        var auditorium = Assert.Single(rooms!, r => r.Name == "Auditorium");
        return auditorium.Id;
    }

    private Task<HttpResponseMessage> BookAsync(int roomId, string start, string end, string title = "Standup")
        => _client.PostAsJsonAsync($"/api/rooms/{roomId}/reservations", new { start, end, title });

    [Fact]
    public async Task Seeds_the_sample_rooms()
    {
        var rooms = await _client.GetFromJsonAsync<List<RoomResponse>>("/api/rooms");

        Assert.Equal(3, rooms!.Count);
        Assert.Contains(rooms, r => r.Name == "Small Room" && r.Capacity == 4);
        Assert.Contains(rooms, r => r.Name == "Conference Room" && r.Capacity == 10);
        Assert.Contains(rooms, r => r.Name == "Auditorium" && r.Capacity == 50);
    }

    [Fact]
    public async Task Accepts_a_booking()
    {
        var roomId = await GetAuditoriumIdAsync();

        var response = await BookAsync(roomId, TenAm, "2026-01-05T11:00:00Z");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal(roomId, created!.RoomId);
        Assert.Equal("Standup", created.Title);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task Rejects_a_booking_that_overlaps_an_existing_one()
    {
        var roomId = await GetAuditoriumIdAsync();
        await BookAsync(roomId, TenAm, "2026-01-05T11:00:00Z");

        // 10:30-11:30 collides with the 10:00-11:00 booking above.
        var response = await BookAsync(roomId, "2026-01-05T10:30:00Z", "2026-01-05T11:30:00Z");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Reservation conflict", problem!.Title);
    }

    [Fact]
    public async Task Accepts_a_back_to_back_booking()
    {
        var roomId = await GetAuditoriumIdAsync();
        await BookAsync(roomId, TenAm, "2026-01-05T11:00:00Z");

        // Touching at 11:00 is not an overlap: ranges are half-open.
        var response = await BookAsync(roomId, "2026-01-05T11:00:00Z", "2026-01-05T12:00:00Z");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Overlap_is_scoped_to_one_room()
    {
        var rooms = (await _client.GetFromJsonAsync<List<RoomResponse>>("/api/rooms"))!;
        var small = rooms.First(r => r.Name == "Small Room");
        var conference = rooms.First(r => r.Name == "Conference Room");

        await BookAsync(small.Id, TenAm, "2026-01-05T11:00:00Z");

        var response = await BookAsync(conference.Id, TenAm, "2026-01-05T11:00:00Z");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Accepts_the_same_start_time_with_a_non_zero_offset()
    {
        var rooms = (await _client.GetFromJsonAsync<List<RoomResponse>>("/api/rooms"))!;
        var small = rooms.First(r => r.Name == "Small Room");

        // 11:00+01:00 is the same instant as 10:00Z; overlap detection must not be fooled by
        // differing textual representations of the same moment.
        var utc = await BookAsync(small.Id, TenAm, "2026-01-05T11:00:00Z");
        var offset = await BookAsync(small.Id, "2026-01-05T11:00:00+01:00", "2026-01-05T12:00:00+01:00");

        Assert.Equal(HttpStatusCode.Created, utc.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, offset.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_timestamp_without_a_utc_offset()
    {
        var roomId = await GetAuditoriumIdAsync();

        var response = await BookAsync(roomId, "2026-01-05T10:00:00", "2026-01-05T11:00:00");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("start", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Rejects_a_reversed_range()
    {
        var roomId = await GetAuditoriumIdAsync();

        var response = await BookAsync(roomId, "2026-01-05T11:00:00Z", TenAm);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_blank_title()
    {
        var roomId = await GetAuditoriumIdAsync();

        var response = await BookAsync(roomId, TenAm, "2026-01-05T11:00:00Z", title: "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_404_for_an_unknown_room()
    {
        var response = await BookAsync(9999, TenAm, "2026-01-05T11:00:00Z");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lists_a_rooms_reservations_limited_to_a_window()
    {
        var roomId = await GetAuditoriumIdAsync();
        await BookAsync(roomId, TenAm, "2026-01-05T11:00:00Z", "Morning");
        await BookAsync(roomId, "2026-01-05T15:00:00Z", "2026-01-05T16:00:00Z", "Afternoon");

        var inMorning = await _client.GetFromJsonAsync<List<ReservationResponse>>(
            $"/api/rooms/{roomId}/reservations?start=2026-01-05T09:00:00Z&end=2026-01-05T12:00:00Z");

        var all = await _client.GetFromJsonAsync<List<ReservationResponse>>(
            $"/api/rooms/{roomId}/reservations");

        Assert.Equal(2, all!.Count);
        var morning = Assert.Single(inMorning!);
        Assert.Equal("Morning", morning.Title);
    }

    [Fact]
    public async Task Requires_start_and_end_together_on_a_window_filter()
    {
        var roomId = await GetAuditoriumIdAsync();

        var response = await _client.GetAsync($"/api/rooms/{roomId}/reservations?start={TenAm}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lists_only_rooms_that_are_free_for_the_whole_window()
    {
        var rooms = (await _client.GetFromJsonAsync<List<RoomResponse>>("/api/rooms"))!;
        var small = rooms.First(r => r.Name == "Small Room");
        var auditorium = rooms.First(r => r.Name == "Auditorium");

        await BookAsync(small.Id, TenAm, "2026-01-05T11:00:00Z");

        var available = await _client.GetFromJsonAsync<List<RoomResponse>>(
            $"/api/rooms?start={TenAm}&end=2026-01-05T11:00:00Z");

        Assert.DoesNotContain(available!, r => r.Id == small.Id);
        Assert.Contains(available!, r => r.Id == auditorium.Id);
    }

    [Fact]
    public async Task Reschedules_a_reservation()
    {
        var roomId = await GetAuditoriumIdAsync();
        var created = await (await BookAsync(roomId, TenAm, "2026-01-05T11:00:00Z"))
            .Content.ReadFromJsonAsync<ReservationResponse>();

        var response = await _client.PutAsJsonAsync($"/api/reservations/{created!.Id}", new
        {
            start = "2026-01-05T14:00:00Z",
            end = "2026-01-05T15:00:00Z",
            title = "Rescheduled",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal("Rescheduled", updated!.Title);
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 14, 0, 0, TimeSpan.Zero), updated.Start);
    }

    [Fact]
    public async Task Rejects_a_reschedule_that_would_overlap_another_reservation()
    {
        var roomId = await GetAuditoriumIdAsync();
        var first = await (await BookAsync(roomId, TenAm, "2026-01-05T11:00:00Z", "First"))
            .Content.ReadFromJsonAsync<ReservationResponse>();
        await BookAsync(roomId, "2026-01-05T14:00:00Z", "2026-01-05T15:00:00Z", "Second");

        var response = await _client.PutAsJsonAsync($"/api/reservations/{first!.Id}", new
        {
            start = "2026-01-05T14:30:00Z",
            end = "2026-01-05T15:30:00Z",
            title = "First",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deletes_a_reservation()
    {
        var roomId = await GetAuditoriumIdAsync();
        var created = await (await BookAsync(roomId, TenAm, "2026-01-05T11:00:00Z"))
            .Content.ReadFromJsonAsync<ReservationResponse>();

        var deleted = await _client.DeleteAsync($"/api/reservations/{created!.Id}");
        var fetched = await _client.GetAsync($"/api/reservations/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_unknown_reservation_is_404()
    {
        var response = await _client.DeleteAsync("/api/reservations/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
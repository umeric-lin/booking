using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Booking.Api.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace Booking.Tests;

/// <summary>
/// Two clients racing for the same slot. Exactly one may win; the rest must be told they lost.
/// </summary>
/// <remarks>
/// This is the case a plain "query, then insert" implementation gets wrong: every request sees a
/// free slot, every request inserts, and the room ends up double-booked. SQLite serialises
/// writers behind <c>BEGIN IMMEDIATE</c>, and the unique index is the backstop, so exactly one
/// insert should survive.
/// </remarks>
public sealed class ConcurrentBookingTests : IAsyncLifetime, IDisposable
{
    private const int Racers = 8;

    private readonly BookingApiFactory _factory = new();
    private readonly ITestOutputHelper _output;
    private HttpClient _client = null!;

    public ConcurrentBookingTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _client = await TestAuth.CreateAuthenticatedClientAsync(_factory);

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _client?.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Exactly_one_of_many_simultaneous_bookings_wins()
    {
        var rooms = (await _client.GetFromJsonAsync<List<RoomResponse>>("/api/rooms"))!;
        var roomId = rooms.First(r => r.Name == "Small Room").Id;

        var attempts = Enumerable.Range(0, Racers).Select(i =>
            _client.PostAsJsonAsync($"/api/rooms/{roomId}/reservations", new
            {
                start = "2026-04-01T10:00:00Z",
                end = "2026-04-01T11:00:00Z",
                title = $"Attempt {i}",
            }));

        var responses = await Task.WhenAll(attempts);

        foreach (var response in responses)
        {
            _output.WriteLine($"{(int)response.StatusCode} {response.StatusCode}");
        }

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var stored = await _client.GetFromJsonAsync<List<ReservationResponse>>(
            $"/api/rooms/{roomId}/reservations");

        Assert.Single(stored!);
    }
}
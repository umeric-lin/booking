using Booking.Core.Entities;

namespace Booking.Api.Contracts;

/// <summary>
/// Times are strings rather than <see cref="DateTimeOffset"/> so that the endpoint can reject
/// input that omits a UTC offset instead of silently guessing the server's zone. See
/// <c>TimeRangeBinding</c>.
/// </summary>
public sealed record CreateReservationRequest(string? Start, string? End, string? Title);

public sealed record UpdateReservationRequest(string? Start, string? End, string? Title);

public sealed record ReservationResponse(
    int Id,
    int RoomId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Title,
    DateTimeOffset CreatedAt)
{
    public static ReservationResponse From(Reservation reservation) => new(
        reservation.Id,
        reservation.RoomId,
        reservation.Start,
        reservation.End,
        reservation.Title,
        reservation.CreatedAt);
}
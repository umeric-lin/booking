namespace Booking.Core.Exceptions;

/// <summary>
/// Thrown when a request is well-formed but breaks one of the booking limits.
/// </summary>
/// <remarks>
/// A different case from <see cref="ReservationConflictException"/>: nothing is wrong with the
/// request's shape, and nothing about the current bookings is in conflict — the request is simply
/// asking for something the system does not do. The API reports it as 400, naming the field at
/// fault so a client can point at the right input.
/// </remarks>
public sealed class BookingRuleViolationException(string field, string message) : Exception(message)
{
    /// <summary>The request field the caller should change, for example <c>end</c>.</summary>
    public string Field { get; } = field;
}
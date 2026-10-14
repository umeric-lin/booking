namespace Booking.Core.Exceptions;

/// <summary>
/// Thrown when a room is addressed by an id that does not exist.
/// </summary>
public sealed class RoomNotFoundException(int roomId)
    : Exception($"Room {roomId} does not exist.")
{
    public int RoomId { get; } = roomId;
}
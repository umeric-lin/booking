using Booking.Core.Entities;

namespace Booking.Api.Contracts;

public sealed record RoomResponse(int Id, string Name, int Capacity)
{
    public static RoomResponse From(Room room) => new(room.Id, room.Name, room.Capacity);
}
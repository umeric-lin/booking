namespace Booking.Core.Entities;

/// <summary>
/// A bookable conference room.
/// </summary>
public sealed class Room
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public int Capacity { get; set; }

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
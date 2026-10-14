using Booking.Core;
using Booking.Core.Entities;

namespace Booking.Application;

/// <summary>
/// Read access to rooms. No rules, no decisions: each method answers one question about stored data.
/// </summary>
public interface IRoomRepository
{
    /// <summary>All rooms, ordered by capacity then name.</summary>
    Task<IReadOnlyList<Room>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The subset of <see cref="ListAsync"/> having no reservation overlapping
    /// <paramref name="window"/>.
    /// </summary>
    Task<IReadOnlyList<Room>> ListWithoutOverlapAsync(
        TimeRange window,
        CancellationToken cancellationToken = default);

    Task<Room?> GetByIdAsync(int roomId, CancellationToken cancellationToken = default);
}
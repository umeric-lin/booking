namespace Booking.Application;

/// <summary>
/// Transaction boundaries and persistence, so that a service can make "check, then write"
/// atomic without knowing what a <c>DbContext</c> is.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Opens a transaction that serialises writers against concurrent ones, so another request
    /// cannot change the data between a check and the write that follows it.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes everything staged through the repositories.
    /// </summary>
    /// <exception cref="Booking.Core.Exceptions.DuplicateReservationStartException">
    /// The database refused a reservation starting at an instant already taken in that room.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// An open transaction. Disposing without committing rolls back.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
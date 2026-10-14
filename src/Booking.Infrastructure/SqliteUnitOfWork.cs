using System.Data;
using Booking.Application;
using Booking.Core.Entities;
using Booking.Core.Exceptions;
using Booking.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Booking.Infrastructure;

/// <summary>
/// Transaction boundaries and persistence for SQLite.
/// </summary>
/// <remarks>
/// Two responsibilities, both of which the application layer cannot perform for itself because
/// they are properties of the database rather than of the booking rules.
/// </remarks>
public sealed class SqliteUnitOfWork(BookingDbContext dbContext) : IUnitOfWork
{
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        // Takes the database write lock up front (BEGIN IMMEDIATE). That is what makes the
        // service's "check for an overlap, then insert" safe: a concurrent request cannot slip in
        // between the conflict check and the write that follows it. A deferred transaction would
        // defer the lock until the first read, letting two requests each observe a free slot.
        //
        // The locking comes from EF calling the non-deferred overload, not from the isolation
        // level. Microsoft.Data.Sqlite 10 exposes deferring as an explicit flag and
        // BEGIN IMMEDIATE is its only transaction statement, so any supported level produces
        // identical SQL; the value here documents the guarantee the service relies on, and gets
        // unsupported levels rejected now rather than silently ignored.
        var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        return new SqliteUnitOfWorkTransaction(transaction);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueStartViolation(exception))
        {
            // The database's unique index on (RoomId, Start) is the backstop for two requests
            // racing for the same slot. Translating the provider-specific error here keeps SQLite
            // out of the application layer; deciding that this means a booking conflict is done
            // by the service.
            var rejected = (Reservation)exception.Entries.Single().Entity;

            throw new DuplicateReservationStartException(rejected.RoomId, rejected.Start);
        }
    }

    private static bool IsUniqueStartViolation(DbUpdateException exception)
        => exception.InnerException is SqliteException { SqliteErrorCode: 19 }
           && exception.InnerException.Message.Contains(
               "IX_Reservations_RoomId_Start", StringComparison.Ordinal);

    private sealed class SqliteUnitOfWorkTransaction(IDbContextTransaction transaction)
        : IUnitOfWorkTransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken = default)
            => await transaction.CommitAsync(cancellationToken);

        // Disposing without a commit rolls back, so an exception anywhere in the service leaves
        // nothing behind.
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
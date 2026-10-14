using Booking.Application;
using Booking.Core;
using Booking.Core.Entities;
using Booking.Core.Exceptions;
using Xunit;

namespace Booking.Tests;

/// <summary>
/// Exercises the booking rules against in-memory fakes.
/// </summary>
/// <remarks>
/// These tests are the point of having a service layer: the overlap rule, the not-found cases and
/// the race-condition translation are all checked without a database, a transaction or HTTP.
/// </remarks>
public sealed class BookingServiceTests
{
    private static readonly DateTimeOffset Ten = new(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rejects_booking_a_room_that_does_not_exist()
    {
        var (service, _, _) = Build();

        await Assert.ThrowsAsync<RoomNotFoundException>(() =>
            service.CreateReservationAsync(42, Range(Ten, 1), "Standup"));
    }

    [Fact]
    public async Task Rejects_a_range_that_overlaps_an_existing_booking()
    {
        var (service, reservations, _) = Build();
        reservations.Seed(RoomId: 1, Ten, 1, "Existing");

        var exception = await Assert.ThrowsAsync<ReservationConflictException>(() =>
            service.CreateReservationAsync(1, Range(Ten, 1.5), "Overlapping"));

        Assert.Equal("Existing", exception.ConflictingReservation.Title);
    }

    [Fact]
    public async Task Accepts_a_range_that_starts_when_the_existing_one_ends()
    {
        var (service, reservations, _) = Build();
        reservations.Seed(RoomId: 1, Ten, 1, "Existing");

        var created = await service.CreateReservationAsync(1, Range(Ten.AddHours(1), 2), "Back to back");

        Assert.Equal("Back to back", created.Title);
    }

    [Fact]
    public async Task Rescheduling_may_overlap_the_reservation_being_edited()
    {
        var (service, reservations, _) = Build();
        var existing = reservations.Seed(RoomId: 1, Ten, 2, "Existing");

        // Widening it overlaps only itself, which is allowed.
        var updated = await service.UpdateReservationAsync(existing.Id, Range(Ten, 3), "Existing");

        Assert.Equal(Ten, updated.Start);
        Assert.Equal(Ten.AddHours(3), updated.End);
    }

    [Fact]
    public async Task Rescheduling_onto_another_booking_is_a_conflict()
    {
        var (service, reservations, _) = Build();
        var existing = reservations.Seed(RoomId: 1, Ten, 1, "Existing");
        reservations.Seed(RoomId: 1, Ten.AddHours(3), 1, "Other");

        var exception = await Assert.ThrowsAsync<ReservationConflictException>(() =>
            service.UpdateReservationAsync(existing.Id, Range(Ten.AddHours(3).AddMinutes(30), 1), "Existing"));

        Assert.Equal("Other", exception.ConflictingReservation.Title);
    }

    [Fact]
    public async Task Losing_the_insert_race_is_reported_as_a_conflict()
    {
        var (service, reservations, unitOfWork) = Build();

        // The database's unique index refuses the insert after the check passed: another request
        // committed first. That must surface as a 409, not a 500.
        unitOfWork.BeforeSave = () => reservations.Seed(RoomId: 1, Ten, 1, "Winner");
        unitOfWork.FailSaveWith = new DuplicateReservationStartException(1, Ten);

        var exception = await Assert.ThrowsAsync<ReservationConflictException>(() =>
            service.CreateReservationAsync(1, Range(Ten, 1), "Loser"));

        Assert.Equal("Winner", exception.ConflictingReservation.Title);
    }

    [Fact]
    public async Task A_booking_longer_than_the_daily_limit_is_rejected()
    {
        var (service, _, _) = Build();

        var exception = await Assert.ThrowsAsync<BookingRuleViolationException>(() =>
            service.CreateReservationAsync(1, Range(Ten, 25), "Too long"));

        Assert.Equal("end", exception.Field);
    }

    [Fact]
    public async Task A_booking_of_exactly_one_day_is_allowed()
    {
        var (service, _, _) = Build();

        var created = await service.CreateReservationAsync(1, Range(Ten, 24), "All day");

        Assert.Equal(Ten.AddHours(24), created.End);
    }

    [Fact]
    public async Task An_availability_window_longer_than_a_week_is_rejected()
    {
        var (service, _, _) = Build();

        var exception = await Assert.ThrowsAsync<BookingRuleViolationException>(() =>
            service.ListAvailableRoomsAsync(Range(Ten, 24 * 8)));

        Assert.Contains("7 days", exception.Message);
    }

    [Fact]
    public async Task Listing_a_rooms_reservations_over_more_than_a_week_is_rejected()
    {
        var (service, _, _) = Build();

        var exception = await Assert.ThrowsAsync<BookingRuleViolationException>(() =>
            service.ListReservationsAsync(1, Range(Ten, 24 * 8)));

        Assert.Contains("7 days", exception.Message);
    }

    [Fact]
    public async Task Listing_a_rooms_reservations_with_no_window_is_unaffected()
    {
        var (service, reservations, _) = Build();
        reservations.Seed(RoomId: 1, Ten, 1, "Old");

        var listed = await service.ListReservationsAsync(1);

        Assert.Single(listed);
    }

    [Fact]
    public async Task Deleting_an_unknown_reservation_is_reported_as_not_found()
    {
        var (service, _, _) = Build();

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            service.DeleteReservationAsync(999));
    }

    [Fact]
    public async Task Stamps_CreatedAt_from_the_injected_clock()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
        var reservations = new FakeReservationRepository();
        var service = new BookingService(
            new FakeRoomRepository(),
            reservations,
            new FakeUnitOfWork(reservations),
            clock);

        var created = await service.CreateReservationAsync(1, Range(Ten, 1), "Standup");

        Assert.Equal(clock.Now, created.CreatedAt);
    }

    private static TimeRange Range(DateTimeOffset start, double hours) => new(start, start.AddHours(hours));

    private static (BookingService Service, FakeReservationRepository Reservations, FakeUnitOfWork UnitOfWork) Build()
    {
        var reservations = new FakeReservationRepository();
        var unitOfWork = new FakeUnitOfWork(reservations);
        var rooms = new FakeRoomRepository();

        var service = new BookingService(rooms, reservations, unitOfWork, TimeProvider.System);

        return (service, reservations, unitOfWork);
    }

    private sealed class FakeRoomRepository : IRoomRepository
    {
        private readonly Room[] _rooms = [new Room { Id = 1, Name = "Small Room", Capacity = 4 }];

        public Task<IReadOnlyList<Room>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Room>>(_rooms);

        public Task<IReadOnlyList<Room>> ListWithoutOverlapAsync(
            TimeRange window, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Room>>(_rooms);

        public Task<Room?> GetByIdAsync(int roomId, CancellationToken cancellationToken = default)
            => Task.FromResult(_rooms.FirstOrDefault(r => r.Id == roomId));
    }

    /// <summary>
    /// Overlap is decided here with <see cref="TimeRange.Overlaps"/>, which is the same rule the
    /// SQL in the real repository implements. Added reservations stay pending until the unit of
    /// work saves, so a rejected insert is not visible to a later read — as with EF Core.
    /// </summary>
    private sealed class FakeReservationRepository : IReservationRepository
    {
        private readonly List<Reservation> _stored = [];
        private readonly List<Reservation> _pending = [];
        private int _nextId = 1;

        public Reservation Seed(int RoomId, DateTimeOffset start, double hours, string title)
        {
            var reservation = new Reservation
            {
                Id = _nextId++,
                RoomId = RoomId,
                Start = start,
                End = start.AddHours(hours),
                Title = title,
            };

            _stored.Add(reservation);
            return reservation;
        }

        /// <summary>Applies everything staged since the last save.</summary>
        public void Flush()
        {
            foreach (var reservation in _pending)
            {
                if (_stored.Remove(reservation))
                {
                    continue;
                }

                reservation.Id = _nextId++;
                _stored.Add(reservation);
            }

            _pending.Clear();
        }

        public Task<IReadOnlyList<Reservation>> ListForRoomAsync(
            int roomId, TimeRange? window = null, CancellationToken cancellationToken = default)
        {
            IEnumerable<Reservation> query = _stored.Where(r => r.RoomId == roomId);

            if (window is { } range)
            {
                query = query.Where(r => r.ToTimeRange().Overlaps(range));
            }

            return Task.FromResult<IReadOnlyList<Reservation>>(query.OrderBy(r => r.Start).ToList());
        }

        public Task<Reservation?> GetByIdAsync(
            int reservationId, CancellationToken cancellationToken = default)
            => Task.FromResult(_stored.FirstOrDefault(r => r.Id == reservationId));

        public Task<Reservation?> FindOverlappingAsync(
            int roomId,
            TimeRange range,
            int? excludeReservationId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_stored.FirstOrDefault(r =>
                r.RoomId == roomId
                && r.Id != excludeReservationId
                && r.ToTimeRange().Overlaps(range)));

        public Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            _pending.Add(reservation);
            return Task.CompletedTask;
        }

        public void Remove(Reservation reservation) => _stored.Remove(reservation);
    }

    private sealed class FakeUnitOfWork(FakeReservationRepository reservations) : IUnitOfWork
    {
        /// <summary>Set to simulate the database refusing an insert.</summary>
        public DuplicateReservationStartException? FailSaveWith { get; set; }

        /// <summary>Runs before the save is attempted or rejected, to stage a competing commit.</summary>
        public Action? BeforeSave { get; set; }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction());

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            BeforeSave?.Invoke();

            if (FailSaveWith is { } failure)
            {
                // Nothing is persisted, and the staged reservation stays invisible to readers.
                throw failure;
            }

            reservations.Flush();
            return Task.CompletedTask;
        }

        private sealed class FakeTransaction : IUnitOfWorkTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
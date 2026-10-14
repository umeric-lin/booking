using Booking.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Infrastructure.Persistence;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Start).IsRequired();
        builder.Property(r => r.End).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();

        // Two ranges sharing a start instant in the same room always overlap, so a duplicate
        // start is never legitimate. The unique index is the last line of defence against two
        // concurrent requests both passing the overlap check: even if the check is bypassed or
        // the transaction is somehow lost, the database refuses the second insert.
        //
        // It is also the access path for every conflict check and range query, because both
        // filter by RoomId first. Ordered by (RoomId, Start) the index narrows to one room and
        // then ranges over Start, so neither query has to scan unrelated rooms.
        builder.HasIndex(r => new { r.RoomId, r.Start })
            .IsUnique()
            .HasDatabaseName("IX_Reservations_RoomId_Start");
    }
}
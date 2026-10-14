using Booking.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Infrastructure.Persistence;

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("Rooms");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Capacity)
            .IsRequired();

        builder.HasMany(r => r.Reservations)
            .WithOne(r => r.Room!)
            .HasForeignKey(r => r.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
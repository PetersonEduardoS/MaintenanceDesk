using Microsoft.EntityFrameworkCore;

namespace MaintenanceDesk.Api;

public class TicketDb(DbContextOptions<TicketDb> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var ticket = modelBuilder.Entity<Ticket>();
        ticket.HasKey(x => x.Id);
        ticket.Property(x => x.Title).HasMaxLength(120);
        ticket.Property(x => x.Equipment).HasMaxLength(80);
        ticket.Property(x => x.Resolution).HasMaxLength(1000);
        ticket.Property(x => x.Version).IsConcurrencyToken();
        ticket.Property(x => x.CreatedAtUtc).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        ticket.Property(x => x.UpdatedAtUtc).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        ticket.HasIndex(x => new { x.Status, x.Priority });
    }
}

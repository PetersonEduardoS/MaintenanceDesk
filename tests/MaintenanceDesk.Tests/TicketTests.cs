using MaintenanceDesk.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MaintenanceDesk.Tests;

public class TicketTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
    private static Ticket NewTicket() => Ticket.Create("Motor overheating", "Conveyor 02", Priority.High, Now);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_title_is_rejected(string? title) =>
        Assert.Throws<ArgumentException>(() => Ticket.Create(title, "Pump", Priority.Low, Now));

    [Fact]
    public void Title_and_equipment_limits_are_enforced()
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(new string('a', 121), "Pump", Priority.Low, Now));
        Assert.Throws<ArgumentException>(() => Ticket.Create("Leak", new string('a', 81), Priority.Low, Now));
        Assert.Throws<ArgumentException>(() => Ticket.Create("Leak", " ", Priority.Low, Now));
        Assert.Throws<ArgumentException>(() => Ticket.Create("Leak", "Pump", (Priority)99, Now));
    }

    [Fact]
    public void Creation_trims_input_and_starts_open()
    {
        var t = Ticket.Create(" Leak ", " Pump ", Priority.Low, Now);
        Assert.Equal("Leak", t.Title);
        Assert.Equal("Pump", t.Equipment);
        Assert.Equal(TicketStatus.Open, t.Status);
        Assert.Equal(1, t.Version);
    }

    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.Open, false)]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Open, false)]
    [InlineData(TicketStatus.InProgress, TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open, false)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Resolved, false)]
    public void Transition_matrix_is_enforced(TicketStatus from, TicketStatus to, bool allowed)
    {
        var t = NewTicket();
        if (from != TicketStatus.Open) t.Transition(TicketStatus.InProgress, null, Now);
        if (from == TicketStatus.Resolved) t.Transition(TicketStatus.Resolved, "Replaced bearing", Now);
        var version = t.Version;
        if (allowed)
        {
            t.Transition(to, "Replaced bearing", Now.AddMinutes(1));
            Assert.Equal(to, t.Status);
            Assert.Equal(version + 1, t.Version);
            Assert.Equal(Now.AddMinutes(1), t.UpdatedAtUtc);
            if (to == TicketStatus.InProgress) Assert.Null(t.Resolution);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => t.Transition(to, "Done", Now));
            Assert.Equal(from, t.Status);
            Assert.Equal(version, t.Version);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolution_requires_notes_and_failed_transition_preserves_state(string? notes)
    {
        var t = NewTicket();
        t.Transition(TicketStatus.InProgress, null, Now);
        Assert.Throws<ArgumentException>(() => t.Transition(TicketStatus.Resolved, notes, Now));
        Assert.Equal(TicketStatus.InProgress, t.Status);
        Assert.Equal(2, t.Version);
    }

    [Fact]
    public void Long_resolution_and_invalid_status_are_rejected()
    {
        var t = NewTicket();
        t.Transition(TicketStatus.InProgress, null, Now);
        Assert.Throws<ArgumentException>(() => t.Transition(TicketStatus.Resolved, new string('a', 1001), Now));
        Assert.Throws<ArgumentException>(() => t.Transition((TicketStatus)99, null, Now));
    }

    [Fact]
    public async Task Sqlite_persists_ticket_and_rejects_concurrent_update()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TicketDb>().UseSqlite(connection).Options;
        await using var first = new TicketDb(options);
        await first.Database.EnsureCreatedAsync();
        var t = NewTicket();
        first.Tickets.Add(t);
        await first.SaveChangesAsync();
        await using var second = new TicketDb(options);
        var stale = await second.Tickets.SingleAsync();
        Assert.Equal(t.Title, stale.Title);
        Assert.Equal(DateTimeKind.Utc, stale.CreatedAtUtc.Kind);
        t.Transition(TicketStatus.InProgress, null, Now);
        await first.SaveChangesAsync();
        stale.Transition(TicketStatus.InProgress, null, Now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}

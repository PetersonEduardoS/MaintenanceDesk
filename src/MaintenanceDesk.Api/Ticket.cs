namespace MaintenanceDesk.Api;

public enum TicketStatus { Open, InProgress, Resolved }
public enum Priority { Low, Medium, High }

public class Ticket
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string Equipment { get; private set; } = "";
    public Priority Priority { get; private set; }
    public TicketStatus Status { get; private set; }
    public string? Resolution { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public int Version { get; private set; }

    private Ticket() { } // EF Core materialization

    public static Ticket Create(string? title, string? equipment, Priority priority, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120)
            throw new ArgumentException("Title must contain 1 to 120 characters.");
        if (string.IsNullOrWhiteSpace(equipment) || equipment.Trim().Length > 80)
            throw new ArgumentException("Equipment must contain 1 to 80 characters.");
        if (!Enum.IsDefined(priority)) throw new ArgumentException("Invalid priority.");
        return new Ticket
        {
            Id = Guid.NewGuid(), Title = title.Trim(), Equipment = equipment.Trim(),
            Priority = priority, Status = TicketStatus.Open, CreatedAtUtc = now,
            UpdatedAtUtc = now, Version = 1
        };
    }

    public void Transition(TicketStatus next, string? resolution, DateTime now)
    {
        if (!Enum.IsDefined(next)) throw new ArgumentException("Invalid status.");
        bool allowed = (Status, next) is (TicketStatus.Open, TicketStatus.InProgress)
            or (TicketStatus.InProgress, TicketStatus.Resolved)
            or (TicketStatus.Resolved, TicketStatus.InProgress);
        if (!allowed) throw new InvalidOperationException($"Cannot change {Status} to {next}.");
        if (next == TicketStatus.Resolved &&
            (string.IsNullOrWhiteSpace(resolution) || resolution.Trim().Length > 1000))
            throw new ArgumentException("Resolving a ticket requires 1 to 1000 characters of resolution notes.");
        Resolution = next == TicketStatus.Resolved ? resolution!.Trim() : null;
        Status = next;
        UpdatedAtUtc = now;
        Version++;
    }
}

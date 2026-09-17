using System.Text.Json.Serialization;
using MaintenanceDesk.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<TicketDb>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("Tickets") ?? "Data Source=maintenance.db"));
var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<TicketDb>().Database.EnsureCreated();

app.MapGet("/api", () => Results.Ok(new
{
    name = "MaintenanceDesk", version = "1.0", scope = "Local portfolio demo",
    endpoints = new[] { "GET /tickets", "GET /tickets/summary", "GET /tickets/{id}", "POST /tickets", "PATCH /tickets/{id}/status" }
}));

app.MapGet("/tickets/summary", async (TicketDb db) =>
{
    var counts = await db.Tickets.GroupBy(t => t.Status)
        .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
    return Results.Ok(new {
        total = counts.Sum(g => g.Count),
        open = counts.Where(g => g.Status == TicketStatus.Open).Sum(g => g.Count),
        inProgress = counts.Where(g => g.Status == TicketStatus.InProgress).Sum(g => g.Count),
        resolved = counts.Where(g => g.Status == TicketStatus.Resolved).Sum(g => g.Count)
    });
});

app.MapPost("/tickets", async (CreateTicket request, TicketDb db, TimeProvider clock) =>
{
    if (request.Priority is null) return Results.Problem(statusCode: 400, title: "Priority is required.");
    try
    {
        var ticket = Ticket.Create(request.Title, request.Equipment, request.Priority.Value, clock.GetUtcNow().UtcDateTime);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return Results.Created($"/tickets/{ticket.Id}", ticket);
    }
    catch (ArgumentException ex) { return Results.Problem(statusCode: 400, title: ex.Message); }
});

app.MapGet("/tickets/{id:guid}", async (Guid id, TicketDb db) =>
    await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id) is { } ticket
        ? Results.Ok(ticket) : Results.NotFound());

app.MapGet("/tickets", async (TicketDb db, string? status, string? priority, int? page, int? pageSize) =>
{
    int current = page ?? 1, size = pageSize ?? 20;
    if (current < 1 || current > 10000 || size < 1 || size > 100)
        return Results.Problem(statusCode: 400, title: "Use page 1-10000 and pageSize 1-100.");
    var query = db.Tickets.AsNoTracking();
    if (status is not null)
    {
        if (!Enum.TryParse<TicketStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
            return Results.Problem(statusCode: 400, title: "Invalid status filter.");
        query = query.Where(t => t.Status == parsed);
    }
    if (priority is not null)
    {
        if (!Enum.TryParse<Priority>(priority, true, out var parsed) || !Enum.IsDefined(parsed))
            return Results.Problem(statusCode: 400, title: "Invalid priority filter.");
        query = query.Where(t => t.Priority == parsed);
    }
    var total = await query.CountAsync();
    var items = await query.OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAtUtc).ThenBy(t => t.Id)
        .Skip((current - 1) * size).Take(size).ToListAsync();
    return Results.Ok(new { items, total, page = current, pageSize = size });
});

app.MapPatch("/tickets/{id:guid}/status", async (Guid id, ChangeStatus request, TicketDb db, TimeProvider clock) =>
{
    if (request.Status is null || request.Version < 1)
        return Results.Problem(statusCode: 400, title: "Status and a positive version are required.");
    var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == id);
    if (ticket is null) return Results.NotFound();
    if (ticket.Version != request.Version)
        return Results.Problem(statusCode: 409, title: "Ticket has changed. Reload it and retry with the current version.");
    try
    {
        ticket.Transition(request.Status.Value, request.Resolution, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        return Results.Ok(ticket);
    }
    catch (ArgumentException ex) { return Results.Problem(statusCode: 400, title: ex.Message); }
    catch (InvalidOperationException ex) { return Results.Problem(statusCode: 409, title: ex.Message); }
    catch (DbUpdateConcurrencyException)
    { return Results.Problem(statusCode: 409, title: "Ticket was changed by another request. Reload it and retry."); }
});

app.Run();

record CreateTicket(string? Title, string? Equipment, Priority? Priority);
record ChangeStatus(TicketStatus? Status, int Version, string? Resolution);

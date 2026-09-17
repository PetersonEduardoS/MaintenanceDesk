# MaintenanceDesk

A small C# / ASP.NET Core API for tracking industrial maintenance tickets. Inspired by the connection between industrial operations and software development.

## Quick start

Requires the .NET 10 SDK. From this directory:

```powershell
dotnet restore
dotnet test
dotnet run --project src/MaintenanceDesk.Api --urls http://localhost:5187
```

In a second PowerShell window, run `./demo.ps1`. It creates fictional equipment data and demonstrates a complete ticket lifecycle. Open http://localhost:5187 for the browser dashboard. The endpoint list is available at /api.

## Rules

- New tickets start as `Open` with version 1.
- Allowed changes: `Open -> InProgress -> Resolved`; `Resolved -> InProgress` reopens a ticket.
- Resolution requires nonblank notes (up to 1000 characters).
- Every successful transition increments `version`.
- Send the version from your last GET/PATCH. An outdated version returns 409; an EF Core concurrency token also protects against two requests racing after their initial reads.
- Title: 1-120 characters; equipment: 1-80; priority: Low, Medium or High.

## HTTP API

| Method | Path | Purpose |
|---|---|---|
| POST | /tickets | Create a ticket; returns 201 and Location |
| GET | /tickets/{id} | Read a ticket; 404 if missing |
| GET | /tickets?status=Open&priority=High&page=1&pageSize=20 | Filter and paginate; highest priority first, oldest first within each priority |
| PATCH | /tickets/{id}/status | Transition with optimistic concurrency |

Create body:

```json
{"title":"Motor overheating","equipment":"Conveyor 02","priority":"High"}
```

Transition body:

```json
{"status":"InProgress","version":1}
```

Resolution body:

```json
{"status":"Resolved","version":2,"resolution":"Replaced the damaged bearing and checked temperature."}
```

Validation failures return 400, invalid transitions or stale updates return 409. Pagination accepts page 1-10000 and pageSize 1-100. Enum request bodies use names, not integers.

## Structure and persistence

`Ticket.cs` owns validation and transition rules, `TicketDb.cs` configures persistence and concurrency, and `Program.cs` maps HTTP requests and errors. Keeping this MVP small makes the business logic easy to follow without unnecessary layers.

SQLite creates `maintenance.db` automatically. Override `ConnectionStrings__Tickets` to choose another database. `EnsureCreated` is used for the first demo; introduce EF migrations before evolving a database you need to retain.

## Verification

19 xUnit cases cover the status matrix, missing and oversized values, notes, reopening, timestamps and versions. The persistence test uses real SQLite and verifies a conflicting write from a second DbContext is rejected. `demo.ps1` additionally exercises the running HTTP API.

## Scope and next steps

This is a local learning/portfolio MVP. It has no authentication, user assignment, audit trail, SLA engine, rate limiting or production deployment. Use fictional data and bind locally. Add identity and authorization before sharing an instance with other users. Future iterations can add an event history, equipment registry.

## Contributing

Keep each change focused. Add a test for a new business rule, run `dotnet test`, and update this README when endpoint behavior changes.

## Browser dashboard

The responsive dashboard in `wwwroot` uses plain HTML, CSS and JavaScript with no frontend build step. Create, start, resolve and reopen work orders; filter by status and priority and paginate results. Summary cards count all work orders, independently of filters, via `GET /tickets/summary`. Resolution notes are required before closing a ticket. Stale updates display a conflict message and refresh the list.

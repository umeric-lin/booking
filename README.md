# Room Booking API

A small REST API for managing conference room reservations, built on .NET 10, ASP.NET Core
minimal APIs, EF Core and SQLite.

## Running it

Requires the .NET 10 SDK.

```bash
dotnet run --project src/Booking.Api
```

Then open <http://localhost:5000> — it redirects to `/swagger`, where every endpoint can be read and
executed. The raw document is at `/swagger/v1/swagger.json`.

The database (`booking.db`) is created and migrated on startup, and the three sample rooms are
seeded if the rooms table is empty. To start over, delete `booking.db` and restart.

```bash
dotnet test          # 68 tests
```

## API

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/auth/token` | Exchange the deployment API key for a bearer token. |
| `GET` | `/api/rooms` | All rooms. With `?start=&end=`, only rooms free for the whole window. **Public.** |
| `GET` | `/api/rooms/{roomId}` | One room. **Public.** |
| `GET` | `/api/rooms/{roomId}/reservations` | A room's reservations. With `?start=&end=`, only those intersecting the window. |
| `POST` | `/api/rooms/{roomId}/reservations` | Book a room. |
| `GET` | `/api/reservations/{reservationId}` | One reservation. |
| `PUT` | `/api/reservations/{reservationId}` | Reschedule and/or retitle. |
| `DELETE` | `/api/reservations/{reservationId}` | Cancel. |

Everything touching reservations requires `Authorization: Bearer <token>`. The room list and the
Swagger documents are public.

Controllers are organised by what they change, not by URL shape: `ReservationsController` owns
every operation that creates or modifies a booking, `RoomsController` owns only reads of rooms.
`POST /api/rooms/{roomId}/reservations` therefore lives in `ReservationsController` despite its
nested path, via an absolute route template — the URL is unchanged, and Swagger groups it under
**Reservations**.

## Authentication

Bearer tokens are HS256 JWTs. Room listings and availability are public — they reveal nothing a
caller should have to authenticate for — while everything touching a booking requires a token.

```bash
TOKEN=$(curl -s -X POST localhost:5000/auth/token \
  -H 'Content-Type: application/json' \
  -d '{"apiKey":"local-dev-key"}' | jq -r .accessToken)

curl -X POST localhost:5000/api/rooms/1/reservations \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"start":"2026-01-05T10:00:00Z","end":"2026-01-05T11:00:00Z","title":"Standup"}'
```

In Swagger, hit `POST /auth/token` first, then use the **Authorize** button.

Two decisions worth naming:

- **Deny by default.** Authorization uses a `FallbackPolicy` requiring an authenticated user, so
  an endpoint is protected unless it opts out with `[AllowAnonymous]`. A newly added action
  cannot accidentally ship unauthenticated — the failure mode is a 401 for a reviewer, not a
  data leak.
- **The token endpoint is a stand-in.** It trades the configured API key for a signed token so
  the API can be exercised without a real identity provider. What it does *not* do is model
  one: there is no user identity behind the key, no consent, no revocation. A real deployment
  points at an OIDC provider, keeps the signing key in a secret store, and validates asymmetric
  tokens — so this endpoint would be deleted rather than hardened. The validation half (issuer,
  audience, signature, lifetime, 30-second clock skew) is the part that stays.

Errors are RFC 9457 problem details: `400` with field-keyed errors for validation and rule
violations, `404` for a missing room or reservation, `409` for an overlap.

## Limits

| Rule | Limit | Applies to |
| --- | --- | --- |
| A single booking may not span more than | 24 hours | `POST` and `PUT` reservations |
| A read window may not span more than | 7 days | `GET /api/rooms?start=&end=` **and** `GET /api/rooms/{roomId}/reservations?start=&end=` |

Both live in `BookingLimits` in `Booking.Core` and are enforced by `BookingService`, not by the
endpoints, so a caller reaching the service another way cannot bypass them. Exactly at the limit
is allowed; one second past it is rejected.

A breach is a **400**, not a 409. Nothing is booked and nothing is occupied — the request is
simply asking for something this system does not do, and the response says which field to change:

```json
{
  "title": "Booking rule violated",
  "status": 400,
  "errors": { "end": ["A query window cannot span more than 7 days."] }
}
```

Limits are checked before any lookup or database work, so a request that is both over-long and
aimed at a nonexistent room is reported as the rule breach it is. This is why the controller does
not check room existence for the listing endpoint: the service raises `RoomNotFoundException` and
the handler renders the same 404, so checking in both places would leave the precedence of the two
rules up to whichever was written first.

The window limit applies only when a window is supplied. `GET /api/rooms` with no query string
returns every room, and `GET /api/rooms/{roomId}/reservations` with no query string returns the
room's whole history; both are unaffected.

Swagger UI is served in every environment, including production. That is a deliberate concession
to this being a review exercise; a real deployment would serve it only in development, or behind
authentication.

`OpenApiDocumentTests` asserts the document still describes every route, contract and status
code, so a dropped endpoint or a missing `WithSummary` cannot pass unnoticed.

### Walkthrough

```bash
curl localhost:5000/api/rooms
# [{"id":1,"name":"Small Room","capacity":4},{"id":2,...},{"id":3,"name":"Auditorium","capacity":50}]

# Book 10:00-11:00
curl -X POST localhost:5000/api/rooms/3/reservations -H 'Content-Type: application/json' \
  -d '{"start":"2026-01-05T10:00:00Z","end":"2026-01-05T11:00:00Z","title":"Standup"}'
# 201 {"id":1,"roomId":3,...}

# 10:30-11:30 overlaps it -> 409, naming the booking it collided with
curl -X POST localhost:5000/api/rooms/3/reservations -H 'Content-Type: application/json' \
  -d '{"start":"2026-01-05T10:30:00Z","end":"2026-01-05T11:30:00Z","title":"Overlapping"}'
# 409 {"title":"Reservation conflict","detail":"Room 3 is already reserved from ..."}

# 11:00-12:00 starts exactly when the first ends -> 201
curl -X POST localhost:5000/api/rooms/3/reservations -H 'Content-Type: application/json' \
  -d '{"start":"2026-01-05T11:00:00Z","end":"2026-01-05T12:00:00Z","title":"Back to back"}'
# 201

# Only the rooms with nothing booked in the window
curl 'localhost:5000/api/rooms?start=2026-01-05T09:00:00Z&end=2026-01-05T12:00:00Z'
# [{"id":1,"name":"Small Room","capacity":4},{"id":2,"name":"Conference Room","capacity":10}]
```

## Layout

```
src/Booking.Core/           Entities, TimeRange, domain exceptions. No dependencies at all.
src/Booking.Application/    The booking rules (BookingService), plus the interfaces it needs:
                            IRoomRepository, IReservationRepository, IUnitOfWork.
src/Booking.Infrastructure/ DbContext, entity configuration, migrations, the SQLite repositories,
                            the unit of work, seeding, DI registration.
src/Booking.Api/            Controllers, request/response contracts, input validation,
                            problem-details mapping.
tests/Booking.Tests/        Domain and service unit tests with fakes, plus HTTP and OpenAPI tests
                            over the real pipeline.
```

Dependencies point one way only: Api → Infrastructure → Application → Core.

The split exists to keep three things apart that used to be tangled in one class:

- **Rules** (does this booking conflict? does this room exist?) live in `BookingService`, and
  depend on nothing but interfaces.
- **Data access** (`SqliteRoomRepository`, `SqliteReservationRepository`) answers questions about
  stored data and stages writes. It opens no transactions and decides nothing.
- **Transaction boundaries** live in `IUnitOfWork`, because "where does a transaction start" is a
  property of the database, not of the booking rules.

The payoff is testability: `BookingServiceTests` checks the overlap rule, the not-found cases and
the lost-race translation against in-memory fakes, with no database and no HTTP.

One detail worth pointing out: SQLite's unique-index violation is translated to
`DuplicateReservationStartException` by the persistence layer — because SQLite error codes are
only meaningful there — and *the service* is what decides that this means a booking conflict and
therefore a 409. Neither layer needs to know the other's reasons.

## Decisions worth explaining

**Half-open intervals.** A reservation is `[Start, End)`. Two ranges conflict when
`existing.Start < requested.End && requested.Start < existing.End`. Touching ranges fail both
comparisons, which is what makes 10:00–11:00 and 11:00–12:00 legal while rejecting 10:30–11:30.
The same predicate answers three questions — "does this booking conflict?", "which reservations
fall in this window?", "which rooms are free?" — so they cannot drift apart.

**Times are UTC.** `TimeRange` normalises to UTC in its constructor, so no caller can forget.
Because every stored value carries the same offset, SQLite's TEXT representation of
`DateTimeOffset` sorts and compares in chronological order.

**Offsets are required on input.** A timestamp without an offset is rejected with a 400 rather
than interpreted in the server's local zone. Otherwise the same request would book different
instants depending on where the server ran, and bookings would drift across a DST boundary.

**Conflict checking is atomic.** Two problems have to be solved together:

1. *Lost updates.* A naive check-then-insert lets two simultaneous requests both see a free slot
   and both insert. The service therefore opens its transaction with
   `IsolationLevel.Serializable`, which Microsoft.Data.Sqlite maps to `BEGIN IMMEDIATE` — the
   write lock is taken up front, so no second writer can interleave between the conflict check
   and the insert that follows it.
2. *The backstop.* A unique index on `(RoomId, Start)` makes a duplicate start impossible. Two
   reservations in the same room can never legitimately share a start instant, so this is a true
   invariant, and it means the database refuses the second insert even if the check were somehow
   bypassed. The service catches that rejection and treats it as the conflict it is, rather than
   letting it surface as a 500.

`tests/Booking.Tests/ConcurrentBookingTests.cs` fires eight simultaneous requests for the same
slot and asserts that exactly one gets a 201, the rest get 409, and one row exists.

**Performance.** Every conflict check and window query is
`WHERE RoomId = ? AND Start < ? AND End > ?`, which is exactly what
`IX_Reservations_RoomId_Start` is shaped for: the index narrows to one room, then ranges over
`Start`. Neither query scans other rooms, and neither loads a room's bookings into memory to
test them in C#. Room availability uses `NOT EXISTS`, so a room appears once regardless of how
many bookings land in the window.

**Validation lives at the edge, the rule lives in the domain.** Controllers parse and validate;
`TimeRange`'s constructor rejects a range that does not move forward as a backstop. The two
binding helpers add their errors to `ModelState`, which the controller returns as a
field-keyed 400. Booking failures are expected outcomes, not incidents, so a conflict is a 409 a
client can act on rather than a 500 that pages someone.

**Controllers, not minimal-API handlers.** Each action is one HTTP verb, declared with
`[HttpGet]`/`[HttpPost]` and an explicit `[ProducesResponseType]` per outcome, and carries XML
documentation that Swagger renders as the operation description and response table. That makes
the contract readable in the source rather than reconstructable from lambdas. Route names
(`Name = "GetReservation"`) are explicit so `CreatedAtRoute` link generation is checked at
startup instead of silently omitting a `Location` header, and action methods are named uniquely
across both controllers so every operation gets a stable `operationId` for client generation.

**Errors.** Domain exceptions are mapped to problem details by one `IExceptionHandler`.
Recognised exceptions become 4xx; anything else is logged with its stack trace and reported as a
500 with no internals leaked.

## What is missing

Left out to stay inside the time box, and how I would approach each:

- **Authentication and authorisation.** Tokens are issued, validated and enforced, and the
  boundary is deny-by-default — but this only answers *whether* a caller is authenticated, not
  *who* they are or *what* they may do. I would put the caller's subject claim on the
  reservation, then authorise per room or per calendar.
- **Who owns a reservation.** There is no user id on a booking, so there is no basis for "my
  bookings", no audit trail, and no way to tell two people apart booking the same room. The token
  carries a `sub` claim that is currently discarded after validation — wiring it through to the
  entity is the first thing I would do next.
- **Multiple instances.** `BEGIN IMMEDIATE` serialises writers within one SQLite file on one
  machine. This design is correct for a single-instance deployment and does not extend to
  several hosts sharing a database — the whole conflict guarantee rests on SQLite's file
  locking. On SQL Server or PostgreSQL the same shape would use `SELECT ... FOR UPDATE` or an
  exclusion constraint on a `tstzrange`, which enforces non-overlap in the database directly and
  is the stronger design.
- **Pagination.** `GET /api/rooms/{id}/reservations` returns a room's whole history. It should
  take a page size and a cursor.
- **Transient retry.** A busy database past the 30-second busy timeout surfaces as a 500. I would
  retry the transaction a bounded number of times on `SQLITE_BUSY` before giving up.
- **Migration discipline.** `Database.Migrate()` runs automatically at startup, which is fine
  locally but wrong for production, where migrations should be a separate gated deployment step.
- **Telemetry and rate limiting.** No metrics, tracing or per-caller quotas.
- **Recurrence, cancellation policy and reminders.** Not in scope here, but all three are needed
  before this is a usable booking system.

## Scaling as data grows

This section expands the pagination, multiple-instances and transient-retry gaps above.

### What exists today

- Exactly one index in the whole schema: `IX_Reservations_RoomId_Start (RoomId, Start)`, unique
  (`src/Booking.Infrastructure/Persistence/ReservationConfiguration.cs`). No index includes `End`
  or `CreatedAt`, and `Rooms` has none beyond its primary key.
- Timestamps are `DateTimeOffset` stored as SQLite `TEXT`. The EF provider can neither compare nor
  `ORDER BY` that type, which is why the overlap queries are hand-written SQL
  (`SqliteReservationRepository.cs`) and why the listing is sorted in application memory.
- Writes take `IsolationLevel.Serializable`, which Microsoft.Data.Sqlite maps to `BEGIN IMMEDIATE`
  (`SqliteUnitOfWork.cs`) — SQLite's single, database-wide write lock.

### The conflict check is O(bookings in that room), not O(log n)

The overlap predicate is:

```sql
WHERE "RoomId" = {0} AND "Start" < {1} AND "End" > {2} LIMIT 1
```

Over `(RoomId, Start)`, `Start < @end` is an **upper-bound range**: SQLite seeks to the first index
entry for that room and range-scans forward. It cannot start at the lower bound, because the lower
bound is on `End` and `End` is not in the index. Three consequences:

1. **`LIMIT 1` does not limit work.** It bounds rows *returned*, not rows *examined*.
2. **The worst case is the common case for a busy room.** A popular room with 10,000 past
   bookings, and a request for a free slot next month, still walks those 10,000 index entries.
3. Each one costs a **table-row fetch** on top of the index seek, because `End` lives in the table.

This runs on every create and every update. With a handful of bookings it is invisible.

**The 24-hour duration limit is what makes it fixable.** `BookingLimits.MaxReservationDuration`
guarantees `e2 - s2 <= 24h`, so any reservation overlapping a window `[a, b)` must have
`End ∈ (a, b + 24h]`. An index on **`(RoomId, End)`** therefore turns the check into a genuine
bounded seek — scanning proportionally to the actual overlaps plus a day of bookings, instead of
to the room's entire history. A rule added for business reasons turns out to be the precondition
for the query being efficient.

### Unbounded listing, sorted in memory

`ListForRoomAsync` has no `LIMIT`, no pagination and no `Take()`. It materialises the whole result
set, sorts it in-process (`O(n log n)`), and the controller maps a second copy before
serialisation. The seven-day cap only applies when a window is supplied — called without one, it
returns a room's entire history. The code anticipates this in a comment: *"The result is already
bounded to a single room, so it stays small. If that assumption ever stops holding…"* That
assumption is exactly what breaks for a hot room.

Pagination does **not** require changing the storage type. The provider blocks the *LINQ*
translation, not SQL — `ORDER BY "Start"` works in the raw SQL these repositories already use. The
seven-day cap also provides a natural upper bound for a keyset cursor on `(Start, Id)`.

### Availability cost grows with rooms × reservations

The availability query runs a correlated `NOT EXISTS` once per room row, so it costs roughly
`#rooms × (index seek + row fetches)`. `NOT EXISTS` is right for correctness — it returns a room
once regardless of how many bookings overlap — but the per-row probe cost is unchanged. Compounding
it, `Rooms` has no index on `Capacity` or `Name`, so both room queries full-scan and temp-sort.
Harmless at three rooms; a real scan at thousands.

The fix is to resolve the busy room ids once (`SELECT DISTINCT RoomId …`, bounded by the seven-day
cap and the index above) and filter rooms against that single set.

### The concurrency ceiling arrives before the disk fills

`BEGIN IMMEDIATE` serialises **all writers across the whole database**, not per room — booking in
one room blocks booking in every other. The lock is held for the entire check-then-write. There is
no application-owned retry or backoff on `SQLITE_BUSY` anywhere in the solution, so contention
surfaces as a 500. `DeleteReservationAsync` has no transaction at all, unlike the other write
paths.

Note that `ConcurrentBookingTests` proves *correctness* under eight simultaneous writers — exactly
one 201, seven 409s. It says nothing about *throughput*, which is the axis in question.

### Housekeeping

- `CreatedAt` is returned in every reservation response, but it is never used in any **query** —
  nothing filters, orders or indexes by it. It earns its place as an audit field, yet costs bytes
  in every row, and in every row fetch the conflict check performs.
- Nothing is ever purged or archived, so both costs above worsen with **age** independently of
  traffic.
- `Title` is declared `maxLength: 200`, which SQLite does not enforce.

### Roadmap, in the order I would pull these levers

| Stage | Trigger | Work |
| --- | --- | --- |
| 0 | Now | Index `(RoomId, End)`; paginate the reservations listing; retry on `SQLITE_BUSY`; give delete a transaction; index `Rooms(Capacity, Name)` |
| 1 | A room with thousands of bookings, or sustained concurrent booking | Page the availability query too; decide whether `CreatedAt` is indexed and used, or dropped |
| 2 | Years of accumulated history | Archive or purge past a horizon; partition by month |
| 3 | Multiple instances, write throughput, or operational maturity | Move to PostgreSQL |

Stage 0 needs no data growth to justify — it is correctness and cost work that pays off
immediately.

**On leaving SQLite (Stage 3).** PostgreSQL, for what it buys:

1. An **exclusion constraint** makes non-overlap a database invariant rather than an application
   check that races:
   ```sql
   EXCLUDE USING gist (room_id WITH =, tstzrange(start, end, '[)') WITH &&)
   ```
   This is strictly stronger than check-then-insert plus a unique index, and it retires the whole
   conflict-scan problem as a *correctness* concern.
2. `SELECT ... FOR UPDATE`, or advisory locks keyed by room, gives per-room serialisation instead
   of a global writer.
3. Native timestamp ordering removes the in-memory sort and lets keyset pagination run in SQL.
4. Real connection pooling.

SQL Server is a reasonable alternative, but it has no range-exclusion constraint, so the conflict
scan remains a query-tuning exercise there rather than a solved problem.
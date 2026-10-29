# Gather

A portfolio event platform. The first milestone is a working, database-backed event catalogue: browse fictional events, filter by category/city/price/local dates, and open an event's details.

**Implemented:** ASP.NET Core 10 API, EF Core migrations, PostgreSQL 18, React/TypeScript/Vite frontend, Docker Compose, PostgreSQL integration tests and browser smoke tests.

**Planned:** customer accounts, organiser tools, capacity-safe reservations, sandbox payments, QR admission, OpenAI-assisted search and AWS deployment. These are not available yet. This demo does not accept real bookings.

## Run locally

Requires Docker Desktop with its Linux engine running.

1. Copy `.env.example` to `.env` and replace the password with a random local password. If this workspace already has `.env`, keep it: the database volume was initialized with that password.
2. Run from this directory:

   ```powershell
   docker compose up --build -d
   ```

3. Open **http://localhost:8080**. The API and frontend share one origin. The database is available only on localhost port `54329`.

The first startup runs migrations and seeds seven fictional November 2026 events: six public and one draft. Restarting preserves data in the `postgres_data` Docker volume. `docker compose down` stops the app while preserving that volume. Removing the volume deletes the local data.

To see startup errors: `docker compose logs app`. To check database connectivity: `http://localhost:8080/api/health`.

## Develop without rebuilding Docker

Use .NET 10 SDK and a Node version supported by Vite (Node 22.12+). The .NET SDK installed for this workspace is under `../../work/runtime/dotnet`; the system `dotnet` currently points to .NET 9.

Start only PostgreSQL with `docker compose up -d postgres`. In one PowerShell terminal:

```powershell
$env:ConnectionStrings__Database = 'Host=localhost;Port=54329;Database=gather;Username=gather;Password=YOUR_LOCAL_PASSWORD'
$env:Database__Initialize = 'true'
$env:Demo__Seed = 'true'
dotnet run --project src/EventPlatform.Api --launch-profile http
```

In another terminal:

```powershell
npm --prefix src/EventPlatform.Web ci
npm --prefix src/EventPlatform.Web run dev
```

Open `http://localhost:5173`. Vite proxies `/api` to port `5226`.

## Verify

With Docker running:

```powershell
dotnet test EventPlatform.sln
npm --prefix src/EventPlatform.Web run build
# Start the Compose application first; the browser tests use http://127.0.0.1:8080.
npm --prefix src/EventPlatform.Web run test:e2e
```

Tests create disposable real PostgreSQL containers. Browser tests use installed Microsoft Edge for desktop and mobile viewport checks. On a machine without Edge, install it with `npx playwright install msedge` from the web directory, or change the test configuration to your available browser. Set `BASE_URL` to test another local origin.

## How the first slice works

```text
React catalogue → GET /api/events → EF Core query → PostgreSQL
Event details   → GET /api/events/{id}
Calendar dates  → GET /api/date-window → UTC bounds → catalogue query
```

The server excludes unpublished events for both list and detail reads. Prices use integer minor units; price filters require a currency. Dates are stored as UTC instants with the event's IANA timezone. Calendar-day conversion happens on the server so a visitor's computer timezone cannot change the selected day. Date upper bounds are exclusive. Pagination is bounded and sorted by start time then ID.

The local city choices currently share `Asia/Kuala_Lumpur`; adding cities in other timezones requires extending that mapping. Ambiguous or missing midnight boundaries return a validation error. Demo dates are fixed in November 2026; update the seed data for later demonstrations on a fresh database.

This Compose file is for local development, not an AWS deployment configuration. No AWS resources or OpenAI calls are required for this milestone. Keep `.env` outside Git.

References: [Vite prerequisites](https://vite.dev/guide/), [PostgreSQL Docker data persistence](https://docs.docker.com/guides/postgresql/immediate-setup-and-data-persistence/).

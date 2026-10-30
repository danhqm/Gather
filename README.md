# Gather

A portfolio event platform. The first milestone is a working, database-backed event catalogue: browse fictional events, filter by category/city/price/local dates, and open an event's details.

**Implemented:** ASP.NET Core 10 API, EF Core migrations, PostgreSQL 18, React/TypeScript/Vite frontend, Docker Compose, customer signup/sign-in, organisation permissions, organiser event management, capacity-safe reservations, PostgreSQL integration tests and browser smoke tests.

**Planned:** sandbox payments, QR admission, OpenAI-assisted search and AWS deployment. These are not available yet. This demo does not accept real bookings.

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

## Accounts and permissions

Use **Sign in → Create an account** to register a customer. Passwords require 12–128 characters with uppercase, lowercase, a number and a symbol. Public signup never creates an organisation membership. Login failures use a generic message; five failed attempts lock the account for 15 minutes, and authentication endpoints allow at most 20 requests per IP per minute.

For local organiser/staff accounts, set `DEMO_ACCOUNT_PASSWORD` in `.env` before starting the app. This workspace already has a generated value in that ignored file. Accounts are `organiser@gather.test` and `staff@gather.test`. Restarting does not reset an existing password. Never publish that `.env` file or use these demo accounts for real customer data.

Identity hashes passwords and issues an HttpOnly session cookie. Production cookies require HTTPS. Local Compose uses HTTP on localhost. Every authenticated mutation, signup and login requires the `X-CSRF-TOKEN` header; fetch its current value from `/api/auth/csrf` after an identity change. Keys used to protect cookies persist in the `data_protection_keys` Docker volume. Treat that volume as sensitive; it is not a replacement for production key protection and backup policy.

Email verification and password recovery are not implemented in this portfolio milestone. Use test addresses. Accounts and events persist in the local PostgreSQL volume.

## Organiser dashboard

Sign in as the local organiser, then choose **Manage events**. Create a draft, edit its details, publish it into discovery and unpublish it again. Public users cannot read drafts. Every management endpoint checks the event's organisation membership on the server; a customer or another organisation's organiser cannot change it.

This demo deliberately supports Kuala Lumpur and George Town, Malaysia time, MYR prices and one general-admission capacity per event. The editor accepts an optional HTTPS image URL; hosted image uploads come later. An empty or failed image uses the illustrated poster. Events must start in the future before publication. The dashboard shows the newest 200 events; pagination is needed before using it for a larger catalogue.

Browser organiser tests read `DEMO_ACCOUNT_PASSWORD` from the ignored local `.env` (or the process environment), and skip that journey if it is absent. Tests run serially because publishing changes the shared local catalogue; they unpublish their demo events when finished.

References: [ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0), [antiforgery tokens](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

## Reservations

Signed-in customers can hold 1–4 admissions for 35 minutes, restore the hold after refreshing, and cancel it. The countdown uses server time even when the browser clock is wrong. Holds are demo reservations; checkout is not enabled yet.

PostgreSQL event-row locks serialize capacity changes. Expired holds stop counting immediately, independently of the cleanup worker. Request IDs make retries return the original booking. Price, capacity and start time become immutable after the first booking; unpublishing blocks new holds and preserves existing ones.


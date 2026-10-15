# Query Explorer

.NET 8 Web API + React (Vite) SPA. Run ad-hoc SELECT queries against SQL Server
or Oracle, page through large result sets without loading them all into memory,
export the full result to CSV, and save/search favorite queries by category.

## Project layout

```
QueryExplorer.sln
src/
  QueryExplorer.Domain          entities & enums, no dependencies
  QueryExplorer.Application     interfaces, DTOs, SQL validation
  QueryExplorer.Infrastructure  SqlServer/Oracle providers, JSON repos, CSV export
  QueryExplorer.Api             ASP.NET Core Web API, controllers, Program.cs
client/                         React + Vite SPA
```

## Prerequisites

- .NET 8 SDK
- Node.js 18+ and npm
- A reachable SQL Server and/or Oracle instance (only needed to actually run
  queries — the app itself starts without one)

## 1. Configure connections

Edit `src/QueryExplorer.Api/data/connections.json` and replace the sample
entries with your real connection strings:

```json
[
  {
    "Id": "11111111-1111-1111-1111-111111111111",
    "Name": "My SQL Server",
    "ProviderType": "SqlServer",
    "ConnectionString": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;"
  }
]
```

`ProviderType` must be `SqlServer` or `Oracle`. Keep the `Id` values (or
generate new GUIDs) — the frontend references connections by this id.

For real deployments, use a **read-only database account** for these
connections — SQL validation on the API is a UX safety net, not a security
boundary.

## 2. Run the API

```bash
cd src/QueryExplorer.Api
dotnet restore
dotnet run
```

The API listens on `http://localhost:5000` by default (check the console
output for the exact port) and serves Swagger at `/swagger` in Development.

> Note: `Oracle.ManagedDataAccess.Core` and `Microsoft.Data.SqlClient` are
> pulled from nuget.org on first `dotnet restore`. If you're behind a
> restricted network/proxy, make sure nuget.org is reachable.

## 3. Run the frontend

```bash
cd client
npm install
npm run dev
```

Open `http://localhost:5173`. If your API runs on a different port, update
`client/.env` (`VITE_API_BASE_URL`).

## Login

The API requires a JWT bearer token for every `/api/*` route except `/api/auth/login`.
On first run, `SqliteUserRepository` creates `src/QueryExplorer.Api/data/users.db`
and seeds one account:

```
username: admin
password: Admin123!
```

**Log in and either change this password or delete the row before exposing the
app beyond localhost.** There's no "forgot password" flow in this starter - to
add another user, log in as an existing user and call `POST /api/auth/register`
(it requires a valid bearer token, so open self-registration isn't possible).

Also set a real, random `Jwt:Key` in `appsettings.json` (or as an environment
variable / secret in production) before deploying - the placeholder value in
this repo is not secret.

The client stores the token in `localStorage` and attaches it as
`Authorization: Bearer <token>` on every API call; a 401 response redirects
back to `/login` automatically.

## Query parameters in favorites

A favorite's SQL can include `{{name}}` placeholders, e.g.:

```sql
SELECT * FROM orders WHERE customer_id = {{customer_id}} AND status = {{status}}
```

When you run that favorite again, the UI detects the placeholders and opens a
form asking for a value for each one before executing. Values are sent to the
API as a separate `parameters` dictionary and bound as real, provider-native
query parameters (`@name` for SQL Server, `:name` for Oracle) inside
`SqlServerQueryProvider`/`OracleQueryProvider` - they are never concatenated
into the SQL text, so a parameter value can't be used to alter the query's
structure.

## Resizing result columns

Drag the thin handle on the right edge of any column header in the results
grid to resize it. Widths are per-session (not persisted) and reset when you
reload the page.

## How it handles large result sets

- Every query the UI runs is server-side paged (`OFFSET/FETCH`, 100 rows at a
  time by default) — the API never materializes the full result set, and the
  browser never holds more than one page.
- Total row count is fetched in parallel with the first page and has its own
  5s timeout, so a slow `COUNT(*)` never blocks the actual results from
  showing.
- "Export CSV (all rows)" streams directly from the database reader to the
  HTTP response, one row at a time, so exporting a multi-million-row result
  doesn't spike server memory.

## Adding a new database provider

1. Add a value to `DatabaseProviderType` (`src/QueryExplorer.Domain/Enums`).
2. Implement `IQueryProvider` in `src/QueryExplorer.Infrastructure/Providers`
   (copy `SqlServerQueryProvider.cs` as a starting point — you mainly need to
   adjust the paging SQL syntax and swap the ADO.NET driver).
3. Register it in `Program.cs`:
   `builder.Services.AddSingleton<IQueryProvider, YourNewProvider>();`
4. Add connection entries with the new `ProviderType` to `connections.json`.

No controller, DTO, or frontend code needs to change.

## Favorites storage

Favorites are stored in `src/QueryExplorer.Api/data/favorites.json`, written
through `JsonFavoriteQueryRepository` behind the `IFavoriteQueryRepository`
interface. This is fine for a single-instance/local setup; if you later run
multiple API instances behind a load balancer, swap this repository for a
real database (the interface doesn't change, so no controller code needs to
change).

## Known gaps / things to harden before production

- No authentication/authorization on the API — add ASP.NET Core auth before
  exposing this beyond localhost.
- Connection strings live in plain JSON — move to environment variables or a
  secrets manager.
- `SqlValidator` blocks obvious non-SELECT statements but is not a full SQL
  parser; the real safety net is a read-only DB role.
- No automated tests included in this scaffold.

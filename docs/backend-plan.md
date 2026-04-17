# xnafiddle Backend — Context & Decisions

This document captures the design decisions, learning context, and progress for adding a backend to xnafiddle.net.

## Project Overview

xnafiddle.net is a browser-based XNA coding environment (jsfiddle for XNA games). Currently deployed as a static site on GitHub Pages. Architecture: Blazor WASM frontend, Monaco editor on the left, KNI (XNA-like) rendering on the right. Compilation happens client-side and supports common libraries.

## Backend Goal

Add a backend to support fiddle storage. This also serves as a learning vehicle for ASP.NET Core / backend development — the user is a 25+ year C# desktop/game developer building backend skills. Originally planned for Azure; pivoted to DigitalOcean due to Azure's lack of hard spend caps.

## Architecture Decisions

### Hosting & Deployment
- **Frontend:** Blazor WASM stays on GitHub Pages
- **Backend:** DigitalOcean App Platform (Basic Web Service, $5/mo) — **Phase 1 (current plan)**
- **Database:** DigitalOcean Dev Database (Postgres) or Neon free tier — **Phase 1**
- **Phase 2 (future):** Migrate to DigitalOcean Droplet ($4/mo) for deeper learning (Linux, systemd, Caddy reverse proxy, self-managed Postgres). The app code doesn't change — only the deployment wrapper.
- **CORS:** Required since frontend (GitHub Pages) and API (DO) are on different domains. Use ASP.NET Core CORS middleware with `WithOrigins("https://xnafiddle.net")`.
- **Why not Azure/AWS:** No hard spend caps — a burst of traffic or misconfiguration can produce an unbounded bill. DO App Platform and Droplets are fixed-monthly-cost. This was a deliberate risk-management decision.

### Repo Layout
- **Monorepo** — backend lives alongside frontend in the existing `XnaFiddle` repo
- Folder structure:
  - `XnaFiddle.BlazorGL/` — existing frontend (Blazor WASM)
  - `XnaFiddle.Api/` — new backend (ASP.NET Core Web API)
- **Project template:** `dotnet new webapi --use-controllers` (matches controllers-not-minimal-APIs decision)

### Versions
- **Backend:** .NET 10 + EF Core 10 (current LTS as of April 2026)
- **Frontend / game runtime:** .NET 8 (locked by KNI dependency until KNI supports .NET 10)
- Mixed-version workflow is fine — user works across many projects on different versions

### Storage Model
- **Anonymous saves only.** No user accounts initially. User gets a URL slug back.
- **Single `Fiddles` table:** `Id`, `Slug`, `Content` (text), `FileReferences` (JSON column - List<string> of URLs), `CreatedAt`
- **Slug:** 7-character random base62 string. Unique constraint on column. Catch duplicate exception and retry.
- **File references:** JSON column storing list of URLs. The local file name used in `Load<Texture2D>("somefile")` is derived from the URL's filename (minus extension) at runtime. Always loaded/saved as a unit with the fiddle, never queried independently.
- **Uploaded files (local):** Not stored on backend for now. Client-side only, ephemeral. Future consideration with blob storage (concerns: abuse, unbounded growth).

### EF Core Column Configuration
- `List<string> FileReferences` mapped to Postgres native `text[]` array column (not JSON — Postgres has real arrays)
- No manual value converter needed — Npgsql handles it natively
- No array-querying needed since file references are always loaded/saved as a unit

### Expiration & Cleanup
- **Window:** 30 days. Monitor data usage; tighten if storage becomes a concern.
- **Policy:** No need to 404 unexpired-but-old fiddles — if the row exists, serve it. Cleanup job deletes rows past the expiration window.
- **Implementation:** Deferred — not needed at launch. Start with manual SQL cleanup; automate later (cron job on Droplet in Phase 2, or a scheduled task in App Platform).

## API Design

- **Style:** Controllers (not minimal APIs). More conventional for interviews; easy to move to minimal APIs later.
- **Endpoints:**
  - `POST /api/fiddles` — sends code content + file reference URLs, returns slug
  - `GET /api/fiddles/{slug}` — returns fiddle content + file references
- **Response shape:** Flat object (no wrapper envelope). Properties directly on the response body.
- **Error shape:** RFC 7807 Problem Details (`application/problem+json`). Use ASP.NET Core's built-in `ProblemDetails` / `ValidationProblemDetails` via `AddProblemDetails()`.

## Concepts Already Discussed

These have been covered in prior conversations and are understood at a working level:

- **IQueryable vs IEnumerable** — expression tree → SQL vs in-memory delegates
- **Slugs** — URL-friendly identifiers, 7-char base62, unique constraint for collision safety
- **Race conditions** — why DB-level unique constraints beat application-level checks
- **EF Core Migrations** — version control for DB schema. C# migration files are source of truth, committed to repo. SQL scripts generated on demand for production deployment, not committed.
- **TTL** — Time To Live, general expiration concept. Redis has it built in, databases don't.
- **CORS** — same-origin policy, preflight OPTIONS requests, ASP.NET middleware config
- **Micro-ORM vs full ORM** — Dapper (write SQL, map results) vs EF Core (generates SQL from LINQ)
- **ASP.NET Core project structure** — Program.cs, pipeline/middleware ordering, controllers vs services folders
- **DI service lifetimes** — singleton / scoped / transient, DbContext is scoped
- **API response shape** — flat vs envelope; RFC 7807 Problem Details for errors
- **Configuration & secrets** — layered config sources (appsettings.json, appsettings.{Env}.json, User Secrets, env vars, CLI args); `:` vs `__` key separators; `appsettings.Development.json` is committed (non-secret defaults), User Secrets for local secrets, Azure App Service Configuration for prod; `ASPNETCORE_ENVIRONMENT` controls which env file loads; Azure Key Vault + managed identity is the grown-up version
- **Options pattern** — bind config sections to POCOs, inject `IOptions<T>`; three flavors (`IOptions`, `IOptionsSnapshot`, `IOptionsMonitor`), default to `IOptions<T>`
- **Program.cs anatomy** — builder/app split around `builder.Build()`; services (DI) + configuration + logging configured on builder; middleware pipeline on app; `AddX` registers capability, `UseX` adds middleware, `MapX` adds endpoints
- **Middleware** — ASP.NET Core sense: pluggable components in the HTTP request/response pipeline (different from game-dev "layered middleware" sense). Order matters; each can short-circuit or call `next()`
- **OpenAPI/Swagger** — OpenAPI = the spec (originated as Swagger 2010, donated to Linux Foundation 2015); Swagger = SmartBear's tooling (UI, Editor, Codegen); `AddOpenApi` registers generator service, `MapOpenApi` exposes it at `/openapi/v1.json`; generated at startup from reflection, cached
- **Routing vocabulary** — URL (concrete client-side string), route (server-side pattern template), endpoint (route + handler pairing), path (URL path portion); `[Route("[controller]")]` token substitutes class name minus `Controller` suffix; `Controller` suffix also triggers framework discovery (or use `[Controller]` attribute explicitly)
- **launchSettings.json** — dev-only, not packaged to prod; multiple profiles selectable at launch; "https" profile typically binds both HTTPS and HTTP ports (HTTP redirects via `UseHttpsRedirection`)
- **Kubernetes / Helm / Bazel / build graphs** — discussed at a surface level as side topics; user flagged for deeper research later (parked: containers, Kubernetes, Helm, ingress, GitOps)
- **EF Core Design package** — `Microsoft.EntityFrameworkCore.Design` is dev-time-only tooling (migration scaffolding, `dotnet ef` commands). Marked `PrivateAssets="all"` so it doesn't ship to production. Same pattern as analyzers/source generators.
- **DbContext** — EF Core's session abstraction. Three jobs: unit of work (change tracking + flush), identity map (same PK = same object within one context), query root (`DbSet<T>` for LINQ). Scoped lifetime in ASP.NET (one per HTTP request).
- **EF Core Fluent API vs Data Annotations** — Fluent API (code in `OnModelCreating`) is preferred over attribute-based config for non-trivial setups. Keeps entities clean, more expressive, some configs are Fluent-only.
- **Builder pattern** — each Fluent API call returns a builder scoped to one element (property, index, relationship). Chain further configuration; start a new chain for a different element.
- **Expression trees in EF** — lambdas like `f => f.Slug` aren't executed, they're *parsed* by EF to extract property names. Same mechanism LINQ-to-SQL uses.
- **`required` modifier (C# 11)** — forces caller to set property at construction/init time. Replaces `= ""` or `= null!` workarounds for non-nullable properties. EF materializes entities via a path that bypasses `required`.
- **Records for DTOs** — `public record FiddleResponse(...)` — concise, immutable, value equality. Works fine with `System.Text.Json` for JSON body binding. Preferred for DTOs in modern .NET.
- **Model binding vs deserialization** — model binding is the broader orchestration (inspect action signature, pick source per parameter, convert/validate). Deserialization (`System.Text.Json`) is one step *inside* model binding, only for body-bound complex types. Not synonyms.
- **Model binding vs data binding** — "model binding" (ASP.NET MVC, one-shot HTTP request → object) vs "data binding" (WPF/MVVM/Blazor, continuous UI ↔ object). Both called "binding," different mechanisms. The "Model" in "model binding" is from MVC, not MVVM.
- **`[ApiController]` attribute** — opts into: automatic 400 on validation failure, Problem Details for errors, complex-type-defaults-to-`[FromBody]`, no explicit `[FromRoute]`/`[FromBody]` needed.
- **`ActionResult<T>`** — return type that allows either a typed value (200 OK) or any `IActionResult` (NotFound, CreatedAtAction, Problem) from the same method.
- **`AsNoTracking()`** — skip EF's change-tracking overhead for read-only queries. Small perf win, universal convention for GET endpoints.
- **`CreatedAtAction`** — returns 201 Created with `Location` header pointing at the GET endpoint for the new resource. HTTP-correct response for POST-that-creates.
- **Exception filters (`catch ... when`)** — C# 6 feature. Exception caught only if the `when` predicate is true; otherwise propagates. Cleaner than catch-and-rethrow.
- **Base62 vs Base64** — Base62 (alphanumeric only) is preferred for URL slugs: no special chars, no URL-encoding, no word-boundary issues on double-click, unambiguous across contexts. Base64URL exists but adds `-` and `_`.
- **Cryptographic vs pseudo-random for slugs** — `RandomNumberGenerator` (crypto) over `Random` (pseudo) for unpredictable slugs. Prevents enumeration/scraping of all fiddles. URL-as-capability pattern (Google Docs "anyone with the link", YouTube unlisted).
- **`WebApplicationFactory<Program>`** — ASP.NET Core's test host. Spins up the app in-memory, returns an `HttpClient`. Industry-standard integration test pattern. Requires `public partial class Program;` to expose the implicit Program class.
- **Provider-neutral DB exception handling** — `EntityFrameworkCore.Exceptions` library provides typed exceptions (`UniqueConstraintException`) instead of provider-specific error number checks. Enables tests to use SQLite while prod uses Postgres.
- **YAGNI vs real-need** — YAGNI cuts against *speculative* abstractions, not ones with concrete near-term payoff. Testability + codebase consistency together are a real-today need ("I need to test the retry loop" + "every other service is DI-injected").
- **PostgreSQL** — open-source, community-governed RDBMS (1986, Berkeley origins). No single owner; contributions from EDB, Crunchy Data, Microsoft, Amazon, Google. PostgreSQL License (MIT/BSD-like). #1 most-wanted DB in Stack Overflow surveys. Killer feature: extension architecture (PostGIS, pgvector, TimescaleDB). Available as managed service on every major cloud (Azure Database for PostgreSQL, AWS RDS, GCP Cloud SQL).
- **Postgres vs SQL Server** — same era (mid-80s), same class (enterprise RDBMS), roughly comparable performance. Postgres wins on: portability, licensing ($0 vs $3.5k+/core), extension architecture, native arrays/JSONB, industry adoption in web/startup world. SQL Server wins on: tooling (SSMS), deep Microsoft ecosystem integration, BI stack. EF Core abstracts dialect differences.
- **SQL dialect differences** — `TOP 1` (T-SQL) vs `LIMIT 1` (Postgres/ANSI); `GETDATE()` vs `NOW()`; `+` vs `||` for string concat; `BIT` vs native `BOOLEAN`. EF Core generates the right SQL per provider so you rarely write raw SQL.
- **SQLite** — embedded library (in-process, single file), not a client-server database. No concurrent writers, no users/roles, no network protocol. Used in tests (fast, in-memory, disposable). Not a production replacement for Postgres.
- **Cloud spend risk** — Azure and AWS have spending *alerts* but no hard spend caps. A burst of traffic or misconfiguration can produce unbounded bills. DigitalOcean Droplets/App Platform are fixed-monthly, solving this for personal/side projects.
- **IaaS vs PaaS vs SaaS** — IaaS: rent a VM, manage everything (DO Droplet, AWS EC2). PaaS: bring your app, provider manages runtime/OS (DO App Platform, Render, Azure App Service). SaaS: just use the app (GitHub, Gmail).
- **Reverse proxy** — a server (Caddy, Nginx) sitting in front of your application server (Kestrel). Handles TLS termination, static assets, compression, request buffering. Industry-universal pattern for production .NET deployments. Kestrel alone is not typically exposed directly to the internet.
- **psql** — Postgres CLI client. `\l` list databases, `\dt` list tables, `\d tablename` describe table, `\q` quit. Worth learning for SSH-accessible servers and interviews.

## Hands-On Progress So Far

Completed:

1. **`XnaFiddle.Api` project scaffolded** (on branch `XnaFiddleApi`)
   - `dotnet new webapi --use-controllers`, added to solution, WeatherForecast scaffolding removed
   - Walked through `Program.cs` line-by-line (builder phase → `Build()` pivot → middleware pipeline → `Run()`)
   - `public partial class Program;` appended so `WebApplicationFactory<Program>` can see it from tests

2. **EF Core wiring**
   - Local `dotnet-ef` tool pinned at `.config/dotnet-tools.json` (10.0.6)
   - Packages: `Npgsql.EntityFrameworkCore.PostgreSQL` + `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets="all"` — dev-only, doesn't ship)
   - `EntityFrameworkCore.Exceptions.PostgreSQL` added for provider-neutral typed DB exceptions (`UniqueConstraintException`, etc.)
   - **Originally built against SQL Server** (user installed full SQL Server 2022 locally). Switched to Postgres when hosting moved from Azure to DigitalOcean. Swap was trivial: package references, one `Program.cs` line (`UseSqlServer` → `UseNpgsql`), connection string format, and re-scaffold the migration. Entity, DTOs, controller, tests — zero changes.

3. **Domain + persistence layer**
   - `Entities/Fiddle.cs` — POCO with `int Id`, `required string Slug`, `required string Content`, `List<string> FileReferences`, `DateTimeOffset CreatedAt`
   - `Data/FiddleDbContext.cs` — `DbSet<Fiddle>`, Fluent API unique index on `Slug`, provider-neutral
   - Migration (`20260417001539_InitialCreate`) scaffolded for Postgres (not yet applied — no local Postgres instance; will apply on DO deployment)
   - Connection string in `appsettings.Development.json` points at `Host=localhost` (placeholder for local Postgres; real connection string will come from DO environment variable in prod)

4. **API surface**
   - `Dtos/CreateFiddleRequest.cs` + `Dtos/FiddleResponse.cs` — records
   - `Slugs/ISlugGenerator.cs` + `Slugs/SlugGenerator.cs` — `RandomNumberGenerator.GetString` over base62, 7 chars, singleton in DI
     - *Originally written as a static helper*; refactored to an interface after user correctly pushed back that testability + codebase consistency were real today-needs, not speculative. Worth remembering: YAGNI cuts against speculative abstractions, not ones with a concrete near-term payoff.
   - `Controllers/FiddlesController.cs` — `[ApiController]`, primary constructor DI (DbContext + ISlugGenerator), `POST` with retry-on-collision loop (catches `UniqueConstraintException`, max 5 attempts, 503 on exhaustion), `GET {slug}` with `AsNoTracking`, `CreatedAtAction` for 201 + Location header, hand-rolled `ToResponse` mapper

5. **Testing** (`XnaFiddle.Api.Tests` project, net10.0, separate from existing `XnaFiddle.Tests` which is net8.0 for the BlazorGL frontend)
   - xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `Microsoft.EntityFrameworkCore.Sqlite` + `Shouldly` + `EntityFrameworkCore.Exceptions.Sqlite`
   - `Infrastructure/ApiFactory.cs` — `WebApplicationFactory<Program>` subclass, swaps SQL Server DbContext for SQLite in-memory (shared `SqliteConnection` singleton for schema persistence across requests), runs `EnsureCreated()` in overridden `CreateHost` (not `ConfigureServices` — that uses a stub provider and fails)
   - `Infrastructure/QueuedSlugGenerator.cs` — test fake returning pre-scripted slugs in order; lets tests force deterministic collisions
   - `SlugGeneratorTests.cs` (3): length = 7, charset ⊂ base62, distinct-across-many-calls
   - `FiddlesControllerTests.cs` (4): `POST` returns 201+Location+body, `POST→GET` round-trip, unknown-slug→404, **retry-on-collision succeeds on next slug** (pre-seeds collision via `QueuedSlugGenerator("AAAAAAA", "AAAAAAA", "BBBBBBB")`)
   - All 7 tests passing

### Gotchas hit along the way (worth remembering)

- **`BuildServiceProvider()` inside `ConfigureServices` is a trap.** It creates a throwaway provider that isn't the host's real one — `EnsureCreated()` against it silently misconfigures. Override `CreateHost` instead.
- **Swapping DbContext providers in tests requires purging all EF services, not just `DbContextOptions<T>`.** `AddDbContext` registers ~100 EF-internal services per provider; leaving the prod SQL Server ones in place while registering SQLite produces the `"Services for database providers 'X', 'Y' have been registered"` error. Fix: remove every descriptor whose `ServiceType.Namespace` starts with `Microsoft.EntityFrameworkCore` before re-adding.
- **SQLite and SQL Server throw different exception types for unique violations** (`SqliteException` code 19 vs `SqlException` 2601/2627). First pass used hand-rolled error-number constants + `SqlException` check — broke the retry test under SQLite. Fixed by adopting `EntityFrameworkCore.Exceptions` library: catch the provider-neutral `UniqueConstraintException` instead. Deleted the hand-rolled `SqlServerErrorNumbers.cs`.
- **SQL Server error numbers have no official .NET enum.** There are ~11k entries in `sys.messages`; Microsoft never shipped constants. If the library approach weren't viable, the convention is a small project-local constants file. (Historical note — no longer applies since we switched to Postgres, but the principle holds for any provider-specific error handling.)
- **Switching EF Core providers is trivial when done early.** SQL Server → Postgres swap touched: packages, one `Program.cs` line, connection string format, migration re-scaffold. Zero changes to entities, DTOs, controller, or tests. This validates the provider-neutral architecture EF Core provides.

## Where We Left Off — Next Steps

1. **Commit current work** on `XnaFiddleApi` branch. Includes: Postgres swap, test project, ISlugGenerator refactor, all API surface code.
2. **CORS wiring** — `AddCors` + `UseCors` with `WithOrigins("https://xnafiddle.net")` for prod; a permissive policy for dev. Discuss preflight OPTIONS requests, `Access-Control-Allow-*` headers, and why the browser enforces CORS but `curl` doesn't.
3. **Problem Details registration** — `builder.Services.AddProblemDetails()` so unhandled exceptions produce RFC 7807 responses automatically (already partially wired via `[ApiController]` for validation; this extends it to uncaught exceptions).
4. **DigitalOcean App Platform deployment (Phase 1)**
   - Create DO account ($200/60-day credit for new accounts)
   - Create App Platform app: Basic Web Service ($5/mo) + Dev Database (Postgres, $7/mo) or Neon free tier
   - Configure environment variable for connection string (`DATABASE_URL` or `ConnectionStrings__FiddleDb`)
   - Deploy via GitHub auto-deploy (App Platform watches the repo)
   - Apply migration against prod Postgres: `dotnet ef migrations script` → review → apply via `psql`, *not* `database update` against prod
5. **Wire the frontend to the API** — Blazor WASM calls to `POST /api/fiddles` and `GET /api/fiddles/{slug}`. Touches the existing `XnaFiddle.BlazorGL` project, introduces typed `HttpClient` DI.
6. **Cleanup job for 30-day expiration** (parked for launch)
7. **README contributor notes** — how to run locally (install Postgres or use Docker), test commands, how to deploy.

### Phase 2 — Droplet migration (when ready for deeper learning)
- Spin up a $4/mo DigitalOcean Droplet (Ubuntu 24.04 LTS)
- Install Postgres, set up `systemd` unit for the .NET app, Caddy as reverse proxy (TLS via Let's Encrypt)
- `pg_dump` from App Platform DB → `pg_restore` on Droplet
- Point DNS at Droplet IP, tear down App Platform
- Learn: SSH, `systemd`, reverse proxies, TLS termination, `ufw` firewall, Postgres administration, `journalctl` log inspection

### Parked topics to revisit when relevant
- Containers / Kubernetes / Helm / ingress / GitOps (surface-discussed only)
- Authentication / user accounts (out of scope for v1 — anonymous fiddles only)
- Secrets management (DO env vars for Phase 1; grow into dedicated secrets manager if needed)

## User Context (for any AI assistant picking this up)

- Strong C# fundamentals (25+ years), DI, async, generics — all transfer directly
- Weak in HTTP/web conventions, cloud infra, JS — this project is a deliberate learning vehicle
- Prefers honest direct feedback, peer collaboration style, no formal teaching
- Wants exposure to standard industry vocabulary and conventions, even if unfamiliar

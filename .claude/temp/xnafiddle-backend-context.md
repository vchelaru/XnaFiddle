# xnafiddle Backend — Context & Decisions

This document captures the design decisions and learning context for adding a backend to xnafiddle.net. Move this file to the xnafiddle repo (e.g., as `BACKEND_NOTES.md` or merged into the repo's `CLAUDE.md`) when starting hands-on work.

## Project Overview

xnafiddle.net is a browser-based XNA coding environment (jsfiddle for XNA games). Currently deployed as a static site on GitHub Pages. Architecture: Blazor WASM frontend, Monaco editor on the left, KNI (XNA-like) rendering on the right. Compilation happens client-side and supports common libraries.

## Backend Goal

Add a backend to support fiddle storage. This also serves as a learning vehicle for ASP.NET Core / Azure backend development — the user is a 25+ year C# desktop/game developer building backend skills.

## Architecture Decisions

### Hosting & Deployment
- **Frontend:** Blazor WASM stays on GitHub Pages
- **Backend:** Azure App Service free tier (60 min/day compute, sleeps on idle, cold start acceptable)
- **Database:** Azure SQL free tier (32GB, 100 DTUs)
- **CORS:** Required since frontend (GitHub Pages) and API (Azure) are on different domains. Use ASP.NET Core CORS middleware with `WithOrigins("https://xnafiddle.net")`.

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

### EF Core JSON Column Configuration
- Use EF Core 10 native primitive collection support
- `List<string> FileReferences` property mapped to `nvarchar(max)` column
- No manual value converter needed — EF handles serialization
- No JSON-querying needed since file references are always loaded/saved as a unit

### Expiration & Cleanup
- **Window:** 30 days. Monitor data usage; tighten if storage becomes a concern.
- **Policy:** No need to 404 unexpired-but-old fiddles — if the row exists, serve it. Cleanup job deletes rows past the expiration window.
- **Implementation:** Deferred — not needed at launch. 32GB is plenty for text fiddles. Start with manual SQL cleanup; automate later (Azure Function on timer trigger is the likely approach since App Service free tier sleeps).

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

## Hands-On Progress So Far

Completed:
1. Created `XnaFiddle.Api` project via `dotnet new webapi --use-controllers -n XnaFiddle.Api` from repo root
2. Added project to the solution (`.sln`) so it's accessible in Visual Studio
3. Verified baseline runs: `https://localhost:7271/weatherforecast` and `/openapi/v1.json` both respond correctly
4. Walked through `Program.cs` line by line (builder phase, `Build()` pivot, middleware pipeline, `Run()`)
5. Deleted template scaffolding: `Controllers/WeatherForecastController.cs` and `WeatherForecast.cs`

## Where We Left Off — Next Steps

The sequence is:

1. **Add EF Core packages** — `Microsoft.EntityFrameworkCore.SqlServer` + `Microsoft.EntityFrameworkCore.Design` (discuss why Design is a separate package)
2. **Create the `Fiddle` entity** — POCO with `Id`, `Slug`, `Content`, `FileReferences` (List<string>), `CreatedAt`
3. **Create `FiddleDbContext`** — with `DbSet<Fiddle>` and Fluent API configuration (unique index on Slug, JSON column for FileReferences)
4. **Wire `AddDbContext` in `Program.cs`** — connection string from config, pointing at local SQL Server (or SQLite for dev simplicity — TBD)
5. **Create and apply the first migration** — `dotnet ef migrations add InitialCreate`, `dotnet ef database update`
6. **Add `FiddlesController`** — `[Route("api/[controller]")]`, `POST` and `GET {slug}` endpoints, DTOs separate from entities
7. **Configuration** — connection string via User Secrets for local dev
8. **Azure deployment** — App Service + Azure SQL, config in App Service Configuration blade

Then revisit parked topics as they become relevant (CORS wiring, Problem Details registration, cleanup job for expired fiddles).

## User Context (for any AI assistant picking this up)

- Strong C# fundamentals (25+ years), DI, async, generics — all transfer directly
- Weak in HTTP/web conventions, cloud infra, JS — this project is a deliberate learning vehicle
- Prefers honest direct feedback, peer collaboration style, no formal teaching
- Wants exposure to standard industry vocabulary and conventions, even if unfamiliar

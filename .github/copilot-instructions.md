# Copilot Instructions

## Repository layout

- `src/` — .NET 10 solution with four projects:
  - `PickleballGenie.Api` — ASP.NET Core Web API (entry point)
  - `PickleballGenie.Data` — EF Core `AppDbContext`, PostgreSQL migrations
  - `PickleballGenie.Models` — Domain models shared across all projects
  - `PickleballGenie.Tests` — xUnit test project
  - `PickleballGenie.Scraper` — Console app that seeds drills into the database
- `ios/PickleballTrainingGenieClient/` — Swift 6 package (iOS 17+, macOS 14+)

> Note: The README references a Node.js `api/` directory; the actual backend is the .NET solution in `src/`.

## Build and test commands

```bash
# Run all .NET tests
dotnet test src/PickleballGenie.Tests/

# Run a single .NET test
dotnet test src/PickleballGenie.Tests/ --filter "FullyQualifiedName~GetRecommendations_ReturnsDrillsWithinDuprRange"

# Run Swift tests
cd ios/PickleballTrainingGenieClient && swift test

# Run a single Swift test
cd ios/PickleballTrainingGenieClient && swift test --filter PickleballTrainingGenieClientTests

# Start the API locally (requires PostgreSQL)
cd src/PickleballGenie.Api && dotnet run

# Docker (includes PostgreSQL via docker-compose)
docker compose up
```

EF Core migrations:

```bash
# Add a migration (run from repo root)
dotnet ef migrations add <MigrationName> --project src/PickleballGenie.Data --startup-project src/PickleballGenie.Api
```

## Architecture

**Dependency chain:** `Models` ← `Data` ← `Api` / `Tests` / `Scraper`

**Database:** PostgreSQL via Npgsql. Migrations run automatically on startup in both `Program.cs` and `Scraper/Program.cs`. Both parse Railway-style `postgres://` connection strings into Npgsql format at startup.

**Auth:** JWT bearer tokens issued by `POST /api/Users/login`. Most controller actions are `[Authorize]`; public endpoints are decorated `[AllowAnonymous]`. The token embeds `ClaimTypes.NameIdentifier` (user GUID) and `CurrentDUPR`.

**Recommendation logic:** `GET /api/Drills/recommendations` returns drills where `TargetDUPRLevel` falls within `[user.CurrentDUPR, user.TargetDUPR]`.

**Swift client:** `PickleballTrainingGenieClient` is a stateful class that stores `jwtToken` after a successful `login()` call and attaches it as a `Bearer` header on all `requireAuth: true` requests.

## Key conventions

- **Request/response DTOs** are defined inline at the bottom of the controller file that uses them (e.g., `RegisterRequest`, `LoginRequest`, `UpdateDuprRequest` in `UsersController.cs`).
- **DUPR** (Dynamic Universal Pickleball Rating) is a `decimal` in .NET and `Decimal` in Swift. It drives drill filtering and recommendations — keep it `decimal`/`Decimal`, not `double`/`float`.
- **Tests** use `EF Core InMemory` with a fresh `Guid`-named database per test and instantiate controllers directly (not via `WebApplicationFactory`).
- **Swift models** implement `Codable, Equatable, Sendable` on all types; the package targets Swift 6 strict concurrency.
- **Drill seeding** is handled by `PickleballGenie.Scraper` (a separate console app), not via EF seed data or migrations.
- Controllers use `[Route("api/[controller]")]` — the route segment is the class name without the `Controller` suffix.

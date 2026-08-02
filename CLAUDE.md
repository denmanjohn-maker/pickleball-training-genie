# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Central Premise

**Every feature in this application must serve one goal: helping pickleball players improve by giving them LLM-generated, level-appropriate drilling workouts.**

The pipeline is:
1. A scraper collects pickleball training drills from the internet and stores them with DUPR-level tags.
2. Users set their DUPR ratings (manually or via DUPR OAuth login), target DUPR, and preferred session duration.
3. When a user requests a workout, the API queries drills matching their DUPR range and sends them to an LLM (DeepInfra-hosted DeepSeek-V3), which returns a structured workout plan with drill sequencing, timing, and coaching notes.

When making any architectural or feature decision, ask: *does this help users get better at pickleball through level-appropriate drilling?* Features that don't serve this goal — social features, general fitness tracking, non-pickleball content — are out of scope.

---

## Commands

```bash
# Run all .NET tests
dotnet test src/PickleballGenie.Tests/

# Run a single .NET test
dotnet test src/PickleballGenie.Tests/ --filter "FullyQualifiedName~GetRecommendations_ReturnsDrillsWithinDuprRange"

# Run Swift tests
cd ios/PickleballTrainingGenieClient && swift test

# Start the API locally (requires PostgreSQL)
cd src/PickleballGenie.Api && dotnet run

# Docker (includes PostgreSQL via docker-compose)
docker compose up

# Seed the drill database
cd src/PickleballGenie.Scraper && dotnet run

# Add an EF Core migration (from repo root)
dotnet ef migrations add <MigrationName> \
  --project src/PickleballGenie.Data \
  --startup-project src/PickleballGenie.Api
```

The API and Scraper both call `MigrateAsync()` on startup, so migrations apply automatically on deploy.

---

## DUPR Level System

DUPR (Dynamic Universal Pickleball Rating) is the skill rating used throughout the app. The six target levels are:

| DUPR | Label | Player Characteristics |
|------|-------|------------------------|
| 2.0 | New to the game | Learning to serve and return, still learning the rules |
| 2.5 | Advanced beginner | Can sustain short rallies, knows the basic rules |
| 3.0 | Beginner | Learning basic strokes, consistency, court positioning |
| 3.5 | Intermediate | Developing third shot drop, kitchen game, transition zone |
| 4.0 | Advanced | Competitive play, pattern recognition, speed-up/reset sequences |
| 5.0 | Professional | Tournament-level, advanced tactics (ATP, Erne), match simulation |

**Drills are tagged with a `TargetDUPRLevel`** (2.0, 2.5, 3.0, 3.5, 4.0, or 5.0). The curated 2.0/2.5 drills are deliberately solo-friendly — players without access to coaching usually lack a drilling partner too. When generating workouts or recommendations, drills are filtered to those where `TargetDUPRLevel >= user.CurrentDUPR AND TargetDUPRLevel <= user.TargetDUPR`. This ensures users practice skills at and just above their current level — the optimal zone for improvement.

**`User.CurrentDUPR` is a computed, `[NotMapped]` property**: `Math.Max(SinglesDUPR ?? 0, DoublesDUPR ?? 0)`. The stored columns are the nullable `SinglesDUPR` and `DoublesDUPR`. Keep DUPR values `decimal` in .NET and `Decimal` in Swift — never `double`/`float`.

---

## Architecture

**Dependency chain:** `Models` ← `Data` ← `Api` / `Scraper` / `Tests`

| Project (in `/src/`) | Type | Role |
|---|---|---|
| `PickleballGenie.Models` | Class library | POCO domain models — no dependencies |
| `PickleballGenie.Data` | Class library | EF Core `AppDbContext` + PostgreSQL migrations |
| `PickleballGenie.Api` | ASP.NET Core Web API | Controllers, auth, DUPR OAuth, LLM integration |
| `PickleballGenie.Scraper` | Console app | Scrapes drill sites + seeds DB |
| `PickleballGenie.Tests` | xUnit test project | Controller tests with in-memory EF database |

**Database:** PostgreSQL via Npgsql. Both `Api/Program.cs` and `Scraper/Program.cs` parse Railway-style `postgres://` connection strings into Npgsql format at startup.

**Auth:** JWT bearer tokens (7-day expiry) issued by `POST /api/Users/login` or `POST /api/Users/dupr-login`. The token embeds `ClaimTypes.NameIdentifier` (user GUID); controllers resolve the current user from that claim.

**iOS client (`/ios/PickleballTrainingGenieClient/`):** Swift 6 package (iOS 17+, macOS 14+) with typed methods mirroring all backend endpoints. It's a stateful class that stores `jwtToken` after `login()` and attaches it as a `Bearer` header on all `requireAuth: true` requests. All Swift models implement `Codable, Equatable, Sendable`; the package targets Swift 6 strict concurrency.

### Endpoints

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/Users/register` | — | Register with email, password, ratings |
| `POST /api/Users/login` | — | Login, returns JWT |
| `POST /api/Users/dupr-login` | — | DUPR OAuth login (creates or links account) |
| `GET /api/Users/profile` | JWT | Current user's profile |
| `PUT /api/Users/profile/ratings` | JWT | Update Singles/Doubles/Target DUPR |
| `GET /api/Drills?category=&level=` | — | Browse drills with optional filters |
| `GET /api/Drills/recommendations` | JWT | Drills within the user's DUPR range |
| `POST /api/Drills/{id}/complete` | JWT | Mark a drill mastered (`UserDrillProgress`) |
| `POST /api/workouts/generate` | JWT | LLM-generated workout plan |

### DUPR OAuth Integration

`Services/DuprService.cs` exchanges an auth code for a token and fetches the user's official DUPR profile (config keys `Dupr:ClientId`, `Dupr:ClientSecret`, `Dupr:RedirectUri`). `POST /api/Users/dupr-login` creates a new account (with `TargetDUPR = max rating + 0.5`) or updates an existing one, setting `IsDuprLinked` and `DuprAccountId`. **DUPR-linked accounts cannot manually override `SinglesDUPR`/`DoublesDUPR`** via `PUT /api/Users/profile/ratings` — only `TargetDUPR`, which must be ≥ `CurrentDUPR`.

---

## Database Schema

### Drills
Core content of the application. Scraped from the internet and manually curated.

| Column | Type | Description |
|---|---|---|
| `Id` | UUID | Primary key |
| `Title` | text | Drill name |
| `Description` | text | Full drill instructions |
| `TargetDUPRLevel` | decimal | One of: 3.0, 3.5, 4.0, 5.0 |
| `Category` | text | Shot type: Dinking, Drops, Volleys, Serving, Returns, Lobs, Resets, Attacking, Movement, General |
| `EstimatedDurationMinutes` | int | Approximate time to complete the drill (default: 10) |
| `VideoUrl` | text? | Optional instructional video link |
| `SourceUrl` | text | Where the drill was sourced from |
| `CreatedAt` | datetime | Insertion timestamp |

### AspNetUsers (extended Identity)
| Column | Type | Description |
|---|---|---|
| `SinglesDUPR` | decimal? | Player's singles rating |
| `DoublesDUPR` | decimal? | Player's doubles rating |
| `TargetDUPR` | decimal | Rating the player wants to reach |
| `PreferredSessionDurationMinutes` | int? | Saved default session length |
| `DuprAccountId` | text? | Official DUPR account id when linked |
| `IsDuprLinked` | bool | True when the account authenticates via DUPR OAuth |
| `CreatedAt` | datetime | Account creation timestamp |
| *(Standard Identity columns)* | | Email, password hash, etc. |

### UserDrillProgresses
Junction table tracking which drills each user has worked on or mastered.

| Column | Type | Description |
|---|---|---|
| `UserId` | UUID FK | References AspNetUsers |
| `DrillId` | UUID FK | References Drills |
| `Status` | int | 0 = InProgress, 1 = Mastered |
| `CompletedAt` | datetime? | When the drill was mastered |

---

## LLM Workout Generation

### Flow (`POST /api/workouts/generate`)
1. Resolve user from JWT claim (`ClaimTypes.NameIdentifier`)
2. Determine session duration: `request.DurationMinutes ?? user.PreferredSessionDurationMinutes ?? 30`
3. Query `Drills` where `TargetDUPRLevel` is within `[CurrentDUPR, TargetDUPR]`, take up to 20
4. Call `IWorkoutLlmService.GeneratePlanAsync` (`Services/WorkoutLlmService.cs`), which builds a prompt from the drill list and user's level context
5. POST to DeepInfra's OpenAI-compatible endpoint (`v1/openai/chat/completions`) with model `deepseek-ai/DeepSeek-V3` (override via `DeepInfra:Model`) and `response_format: json_object`
6. Deserialize `choices[0].message.content` into `WorkoutPlanResponse`; the controller maps service exceptions to 503 (missing key) or 502 (API error / unparseable response)

### Prompt Design Principle
The system prompt instructs the model to act as an expert pickleball coach and return only valid JSON. The user prompt includes the player's current and target DUPR (with human-readable labels), total session duration, the numbered drill list, and the exact JSON schema the model must conform to.

To modify the workout format, update the prompt built inside `DeepInfraWorkoutLlmService.GeneratePlanAsync` and the corresponding response DTOs (`WorkoutDrillItem`, `WorkoutPlanResponse` in `WorkoutsController.cs`).

### Configuration
- `DEEPINFRA_API_KEY` env var (or `DeepInfraApiKey` in `appsettings.json`) — sent as a `Bearer` token
- `DeepInfra:Model` (optional) — defaults to `deepseek-ai/DeepSeek-V3`
- `DeepInfra:BaseUrl` (optional) — defaults to `https://api.deepinfra.com/`; the typed HttpClient is registered in `Program.cs`

---

## Scraper

File: `src/PickleballGenie.Scraper/Program.cs`

### How It Works
1. Connects to the database and runs any pending EF migrations
2. Attempts to scrape configured pickleball sites using `HtmlAgilityPack`
3. Falls back to a curated list of high-quality drills covering all DUPR levels and categories
4. Skips drills whose `Title` already exists in the database (idempotent)

Drill seeding is handled only by the Scraper — not via EF seed data or migrations.

### DUPR Mapping
Scraped drills are assigned a DUPR level via keyword heuristics on title + description:
- "pro", "professional", "tournament" → 5.0
- "advanced", "competitive", "4.0" → 4.0
- "intermediate", "3.5", "transition" → 3.5
- (default) → 3.0

### Adding New Drill Sources
1. Add the target URL to the `sites` array in `Main()`
2. Verify the site's HTML structure and adjust `ScrapeSiteAsync()` XPath selectors if needed
3. The `BuildDrill()` helper handles DUPR mapping, duration estimation, and category assignment automatically

---

## Conventions

- **Request/response DTOs** are defined inline at the bottom of the controller file that uses them (e.g., `RegisterRequest`, `LoginRequest`, `UpdateRatingsRequest` in `UsersController.cs`).
- **New endpoints** follow the existing controller pattern: `[ApiController]`, `[Route("api/[controller]")]` (route segment = class name minus `Controller`), `[Authorize]` on authenticated actions, `AppDbContext` injected via constructor, typed action results (`Ok(...)`, `NotFound(...)`, `BadRequest(...)`).
- **Tests** use EF Core InMemory with a fresh `Guid`-named database per test and instantiate controllers directly (not via `WebApplicationFactory`). Follow the pattern in `DrillsControllerTests.cs`.

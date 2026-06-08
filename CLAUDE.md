# Pickleball Training Genie — Developer Guide for LLMs

## Central Premise

**Every feature in this application must serve one goal: helping pickleball players improve by giving them LLM-generated, level-appropriate drilling workouts.**

The pipeline is:
1. A scraper collects pickleball training drills from the internet and stores them with DUPR-level tags.
2. Users set their current DUPR rating, target DUPR rating, and preferred session duration in their profile.
3. When a user requests a workout, the API queries drills matching their DUPR range and sends them to Claude, which returns a structured workout plan with drill sequencing, timing, and coaching notes.

When making any architectural or feature decision, ask: *does this help users get better at pickleball through level-appropriate drilling?* Decisions that do not support this premise should be questioned.

---

## DUPR Level System

DUPR (Dynamic Universal Pickleball Rating) is the skill rating used throughout the app. The four target levels are:

| DUPR | Label | Player Characteristics |
|------|-------|------------------------|
| 3.0 | Beginner | Learning basic strokes, consistency, court positioning |
| 3.5 | Intermediate | Developing third shot drop, kitchen game, transition zone |
| 4.0 | Advanced | Competitive play, pattern recognition, speed-up/reset sequences |
| 5.0 | Professional | Tournament-level, advanced tactics (ATP, Erne), match simulation |

**Drills are tagged with a `TargetDUPRLevel`** (3.0, 3.5, 4.0, or 5.0). When generating workouts, drills are filtered to those where `TargetDUPRLevel >= user.CurrentDUPR AND TargetDUPRLevel <= user.TargetDUPR`. This ensures users practice skills at and just above their current level — the optimal zone for improvement.

---

## Architecture

### Projects (in `/src/`)

| Project | Type | Role |
|---|---|---|
| `PickleballGenie.Models` | Class library | POCO domain models — no dependencies |
| `PickleballGenie.Data` | Class library | EF Core DbContext + Migrations — depends on Models |
| `PickleballGenie.Api` | ASP.NET Core Web API | Controllers, auth, LLM integration — depends on Data + Models |
| `PickleballGenie.Scraper` | Console app | Scrapes drill sites + seeds DB — depends on Data + Models |
| `PickleballGenie.Tests` | xUnit test project | Integration tests with in-memory DB — depends on Api + Data |

### iOS Client (`/ios/PickleballTrainingGenieClient/`)
Swift 6 package targeting iOS 17+ with typed API client methods mirroring all backend endpoints.

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
| `CurrentDUPR` | decimal | Player's current DUPR rating |
| `TargetDUPR` | decimal | Rating the player wants to reach |
| `PreferredSessionDurationMinutes` | int? | Saved default session length |
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

### Endpoint
`POST /api/workouts/generate` — requires JWT auth.

### Flow
1. Resolve user from JWT claim (`ClaimTypes.NameIdentifier`)
2. Determine session duration: `request.DurationMinutes ?? user.PreferredSessionDurationMinutes ?? 30`
3. Query `Drills` where `TargetDUPRLevel` is within `[CurrentDUPR, TargetDUPR]`, take up to 20
4. Build a prompt including the drill list (title, category, duration, DUPR, description) and the user's level context
5. POST to `https://api.anthropic.com/v1/messages` with model `claude-sonnet-4-6`
6. Deserialize the JSON response into `WorkoutPlanResponse`; fall back to `{ rawResponse }` on parse failure

### Prompt Design Principle
The system prompt instructs Claude to act as an expert pickleball coach and return only valid JSON. The user prompt includes:
- Player's current and target DUPR (with human-readable labels)
- Total session duration
- The numbered drill list
- The exact JSON schema Claude must conform to

To modify the workout format, update `BuildWorkoutPrompt` in `WorkoutsController.cs` and the corresponding response DTOs (`WorkoutDrillItem`, `WorkoutPlanResponse`).

### Configuration
- `ANTHROPIC_API_KEY` env var (or `AnthropicApiKey` in `appsettings.json`)
- The API key is injected into each request as the `x-api-key` header
- The `anthropic-version: 2023-06-01` header is set on the named HttpClient registered in `Program.cs`

---

## Scraper

File: `src/PickleballGenie.Scraper/Program.cs`

### How It Works
1. Connects to the database and runs any pending EF migrations
2. Attempts to scrape configured pickleball sites using `HtmlAgilityPack`
3. Falls back to a curated list of high-quality drills covering all DUPR levels and categories
4. Skips drills whose `Title` already exists in the database (idempotent)

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

## Key Files

| File | Purpose |
|---|---|
| `src/PickleballGenie.Models/Drill.cs` | Drill domain model |
| `src/PickleballGenie.Models/User.cs` | User domain model |
| `src/PickleballGenie.Data/AppDbContext.cs` | EF Core context |
| `src/PickleballGenie.Api/Controllers/WorkoutsController.cs` | LLM workout generation |
| `src/PickleballGenie.Api/Controllers/DrillsController.cs` | Drill browsing + recommendations |
| `src/PickleballGenie.Api/Controllers/UsersController.cs` | Auth + user profile |
| `src/PickleballGenie.Api/Program.cs` | App startup, DI registration |
| `src/PickleballGenie.Scraper/Program.cs` | Drill scraping + DB seeding |

---

## Development Guidelines

### Adding a migration
```bash
dotnet ef migrations add <MigrationName> \
  --project src/PickleballGenie.Data \
  --startup-project src/PickleballGenie.Api
```
The API and Scraper both call `MigrateAsync()` on startup, so migrations apply automatically on deploy.

### Adding a new endpoint
Follow the pattern in existing controllers:
- `[ApiController]`, `[Route("api/[controller]")]`
- `[Authorize]` for authenticated endpoints
- Inject `AppDbContext` via constructor
- Return typed action results (`Ok(...)`, `NotFound(...)`, `BadRequest(...)`)

### Running tests
```bash
dotnet test src/PickleballGenie.Tests/
```
Tests use an in-memory EF database. New controller tests should follow the pattern in `DrillsControllerTests.cs`.

### Decision principle
Before adding any feature, confirm it serves the core premise: *helping pickleball players improve through LLM-generated, level-appropriate drilling workouts.* Features that don't serve this goal — social features, general fitness tracking, non-pickleball content — are out of scope.

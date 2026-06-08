# Pickleball Training Genie

Pickleball Training Genie scrapes the internet for pickleball training drills, categorizes them by DUPR skill level, and uses an LLM to generate personalized drilling workouts. A user sets their DUPR rating and available training time; the application produces a level-appropriate workout plan powered by Claude AI.

## Core Concept

1. **Drill Database** — The scraper collects drills from pickleball sites and tags each with a DUPR level:
   | DUPR | Level |
   |------|-------|
   | 3.0 | Beginner |
   | 3.5 | Intermediate |
   | 4.0 | Advanced |
   | 5.0 | Professional |

2. **User Profile** — Users register with their current DUPR and target DUPR, plus an optional preferred session duration.

3. **AI Workout Generation** — `POST /api/workouts/generate` queries drills matching the user's DUPR range, sends them to Claude, and returns a structured workout plan (drill sequence, per-drill coaching notes, warmup, cooldown).

## Repository layout

- `/src` – .NET 10 solution containing the backend:
  - `PickleballGenie.Api` – ASP.NET Core Web API (entry point).
  - `PickleballGenie.Data` – Entity Framework Core context and migrations.
  - `PickleballGenie.Models` – Shared domain models (Drill, User, UserDrillProgress).
  - `PickleballGenie.Scraper` – Console app that scrapes and seeds the drill database.
  - `PickleballGenie.Tests` – xUnit integration tests.
- `/ios/PickleballTrainingGenieClient` – Swift 6 package with models and API client methods.

## API endpoints

**Authentication & Users**
- `POST /api/Users/register` – Register with email, password, currentDUPR, targetDUPR, and optional preferredSessionDurationMinutes.
- `POST /api/Users/login` – Login and receive a JWT Bearer token (7-day expiry).
- `PUT /api/Users/{id}/dupr` – Update DUPR ratings.

**Drills**
- `GET /api/Drills?category=...&level=...` – Browse all drills (optional filters).
- `GET /api/Drills/recommendations` – Drills matched to the authenticated user's DUPR range. *(Requires Auth)*
- `POST /api/Drills/{id}/complete` – Mark a drill as mastered. *(Requires Auth)*

**Workouts**
- `POST /api/workouts/generate` – Generate an AI-powered workout plan. *(Requires Auth)*
  - Body: `{ "durationMinutes": 45 }` (omit to use the user's saved preference, defaults to 30)
  - Response: `{ drills, totalDuration, warmup, cooldown, coachingNotes }`

## Environment variables

| Variable | Description |
|---|---|
| `ANTHROPIC_API_KEY` | Required for workout generation via Claude |
| `JWT_SECRET` | JWT signing secret (use a long random string in production) |
| `DATABASE_URL` | PostgreSQL connection string (Railway `postgres://` format supported) |

## Running the API locally

### Using Docker Compose

```bash
docker compose up
```
The API will be available at `http://localhost:8080`.

### Using .NET CLI

Ensure a local PostgreSQL instance is running on port `5432` with username `postgres` and password `postgres` (or set `DATABASE_URL`).

```bash
cd src/PickleballGenie.Api
dotnet run
```
The API starts and automatically runs any pending database migrations.

## Seeding the Drill Database

The scraper fetches real drills from pickleball websites and falls back to a curated set of drills spanning all DUPR levels (3.0–5.0) and categories. Re-running is idempotent — duplicates are skipped by title.

```bash
cd src/PickleballGenie.Scraper
dotnet run
```

Set `DATABASE_URL` if your database isn't on the default local connection.

## Running Tests

**Backend (.NET):**
```bash
dotnet test src/PickleballGenie.Tests/
```

**iOS Client (Swift):**
```bash
cd ios/PickleballTrainingGenieClient
swift test
```

## Deployment (Railway)

The API parses Railway's `DATABASE_URL` (`postgresql://...` or `postgres://...`) into the correct Npgsql format and runs migrations on startup. Set `ANTHROPIC_API_KEY` and `JWT_SECRET` as Railway environment variables.

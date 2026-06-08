# Pickleball Training Genie

This repository provides a complete foundation for a Pickleball training application, including:

- A robust .NET 10 API backed by PostgreSQL to track users, DUPR ratings, and drill recommendations.
- An iOS-ready Swift package that provides a strongly typed client for the API.

## Repository layout

- `/src` – .NET 10 solution containing the backend:
  - `PickleballGenie.Api` – ASP.NET Core Web API (entry point).
  - `PickleballGenie.Data` – Entity Framework Core context and migrations.
  - `PickleballGenie.Models` – Shared domain models (Drill, User, UserDrillProgress).
  - `PickleballGenie.Tests` – xUnit test project using an in-memory database.
  - `PickleballGenie.Scraper` – Console application that seeds the database with drills.
- `/ios/PickleballTrainingGenieClient` – Swift 6 package with models and API client methods.

## API endpoints

**Authentication & Users**
- `POST /api/Users/register` – Register a new user with email, password, and DUPR rating.
- `POST /api/Users/login` – Login and receive a JWT Bearer token.
- `PUT /api/Users/{id}/dupr` – Update a user's DUPR rating.

**Drills**
- `GET /api/Drills?category=...&level=...` – Get all drills (optionally filtered).
- `GET /api/Drills/recommendations` – Get drills tailored to the authenticated user's DUPR rating. *(Requires Auth)*
- `POST /api/Drills/{id}/complete` – Mark a drill as mastered for the authenticated user. *(Requires Auth)*

## Running the API locally

The API requires a PostgreSQL database. You can run the entire stack (API + Postgres) via Docker Compose, or run them locally.

### Using Docker Compose

```bash
docker compose up
```
The API will be available at `http://localhost:8080`.

### Using .NET CLI

Ensure you have a local PostgreSQL instance running and listening on port `5432` with username `postgres` and password `postgres` (or override `DATABASE_URL` / `DefaultConnection`).

```bash
cd src/PickleballGenie.Api
dotnet run
```
The API will start and automatically run any pending database migrations.

### Seeding Data

To seed the database with initial drills:
```bash
cd src/PickleballGenie.Scraper
dotnet run
```

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

The API is fully configured for deployment on [Railway](https://railway.app). 
It automatically parses Railway's provided `DATABASE_URL` (`postgresql://...` or `postgres://...`) into the correct Npgsql connection string format and handles migrations on startup.

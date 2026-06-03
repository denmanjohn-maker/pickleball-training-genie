# Pickleball Training Genie

This repository now includes a minimal foundation for:

- a JSON API that tracks pickleball training sessions and returns drill recommendations
- an iOS-ready Swift package that can call that API

## Repository layout

- `/api` – Node.js API with in-memory drill and training session data
- `/ios/PickleballTrainingGenieClient` – Swift package with models and API client methods

## API endpoints

- `GET /health`
- `GET /api/drills`
- `GET /api/recommendations?skillLevel=beginner&focus=control`
- `GET /api/training-sessions`
- `POST /api/training-sessions`

Example training session payload:

```json
{
  "playerName": "Alex",
  "skillLevel": "intermediate",
  "focus": "offense",
  "durationMinutes": 30,
  "completedDrillIds": ["speed-up-counter"]
}
```

## Running the API

```bash
cd api
npm install
npm test
npm start
```

The API starts on `http://localhost:3000`.

## Building the Swift client

```bash
cd ios/PickleballTrainingGenieClient
swift test
```

Use `PickleballTrainingGenieClient` from an iOS app target to fetch drills, recommendations, and recorded sessions from the API.
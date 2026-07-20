# TheLeague API

ASP.NET Core 10 API for The League.

## Projects

- `TheLeague`: core models, enums, and service interfaces.
- `TheLeague.Mongo`: persistence-facing services. The first slice uses an in-memory context with Mongo settings in place for the Atlas-backed implementation.
- `TheLeague.Web`: API endpoints, cookie authentication, CORS, and OpenAPI in development.
- `TheLeague.Tests`: NUnit service and architecture tests.

## Run locally

```bash
dotnet run --project TheLeague.Web
```

The frontend expects the API at `http://localhost:5000/api` by default.

## First vertical slice

Implemented flow:

1. Register and sign in.
2. Create a league with a join code.
3. Join a league by code.
4. Owner/admin creates a fixed challenge.
5. Participant submits points.
6. Owner/admin approves the submission.
7. Approval creates an immutable point allocation.
8. Leaderboard totals and Points Feed are calculated from allocations.

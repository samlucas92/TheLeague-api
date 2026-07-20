# TheLeague API

ASP.NET Core 10 API for The League.

## Projects

- `TheLeague`: core models, enums, and service interfaces.
- `TheLeague.Mongo`: persistence-facing services backed by MongoDB Atlas when configured, with an in-memory fallback for local/test use.
- `TheLeague.Web`: API endpoints, cookie authentication, CORS, and OpenAPI in development.
- `TheLeague.Tests`: NUnit service and architecture tests.

## Run locally

```bash
dotnet run --project TheLeague.Web
```

The frontend expects the API at `http://localhost:5000/api` by default.

## Render configuration

Set these environment variables on the Render API service:

```bash
ASPNETCORE_ENVIRONMENT=Production
PORT=8080
Mongo__ConnectionString=<your MongoDB Atlas connection string>
Mongo__DatabaseName=the-league
Auth__TokenSigningKey=<a long random secret>
Cors__AllowedOrigins__0=<your Vercel frontend origin, e.g. https://your-app.vercel.app>
```

The production auth cookie is sent as `SameSite=None; Secure`, so the Vercel frontend must call the API over HTTPS with credentials included.

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

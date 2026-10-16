# Dalan backend

`Dalan.sln` and `src/` in this directory are the provisional canonical implementation. All projects target .NET 9.

## First feature: vehicle registration and retrieval

The dependency flow is controller → service interface → Application service → repository interface → Infrastructure repository → EF Core DbContext → SQL Server. Controllers and services do not use DbContext. Request/response DTOs live in Contracts; Domain entities are not returned from the API.

Vehicle fields are a generated UUID and a registration number. Registration numbers are required, trimmed, uppercased invariantly, and limited to 32 UTF-16 characters after normalization. No country-specific format, vehicle category, capacity, driver, or marketplace policy is imposed. Normalized numbers are unique; the SQL Server unique index also resolves concurrent registration attempts.

| InternalApi route | Behavior |
|---|---|
| `POST /api/vehicles` | Body: `{"registrationNumber":"abc-123"}`. Returns `201` with `{ "id": "<uuid>", "registrationNumber": "ABC-123" }` and a retrieval Location header; `400` validation problem for invalid input; `409` problem for duplicate registration. |
| `GET /api/vehicles/{id}` | Returns the vehicle DTO with `200`, or `404` when absent. The route requires a UUID. |

Existing weather endpoints remain. MVC, authentication/roles, drivers, deliveries, Hangfire processing, routing, Android, and marketplace behavior are not implemented in this milestone. These vehicle endpoints currently have no authentication policy.

## Development setup

Run these commands from this `backend/` directory:

```powershell
dotnet restore Dalan.sln
dotnet tool restore
dotnet build Dalan.sln --no-restore
```

Development configuration targets `localhost`, database `Dalan.Development`, using Windows integrated authentication. The local development certificate is trusted through `TrustServerCertificate=True`; this is a development setting. No credentials are committed. To select another development database, override the connection string:

```powershell
dotnet user-secrets set "ConnectionStrings:Dalan" "Server=YOUR_SERVER;Database=YOUR_DEV_DATABASE;Integrated Security=True;Encrypt=True;TrustServerCertificate=True" --project src/Dalan.InternalApi
```

After selecting the intended development database, apply the initial migration explicitly:

```powershell
dotnet tool run dotnet-ef -- database update --project src/Dalan.Infrastructure --startup-project src/Dalan.InternalApi -- --environment Development
dotnet run --project src/Dalan.InternalApi --launch-profile https
```

The app does not migrate databases automatically. The implementation session applied migrations only to isolated test databases, not to `Dalan.Development` or an existing application database.

Outside Development, supply `ConnectionStrings__Dalan` through environment/deployment configuration; startup fails clearly if the setting is missing. Use an appropriate trusted SQL Server certificate for deployment. Authentication and deployment hardening remain later work.

## Tests

```powershell
dotnet test tests/Dalan.UnitTests --no-restore
dotnet test tests/Dalan.IntegrationTests --no-restore
# Or run both:
dotnet test Dalan.sln --no-restore
```

Unit tests require no database. Integration tests require a reachable SQL Server and permission to create/drop a test database. By default they use `localhost` with Windows authentication. To choose a test server, set `DALAN_TEST_SQLSERVER` in the test process environment. Never put credentials into committed files.

The fixture always replaces the supplied database name with a new `DalanTests_<uuid>` name, applies the actual migration, and deletes only that generated database during teardown. Tests do not silently skip when SQL Server is unavailable. They exercise repository persistence, simultaneous duplicate inserts, constraints, API validation, Location/retrieval, duplicate responses, and missing vehicles.

Verified on 2026-10-16: 10 unit tests and 9 SQL Server/API integration tests pass.

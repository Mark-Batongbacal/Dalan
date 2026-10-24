# Dalan backend

`Dalan.sln` and `src/` in this directory are the provisional canonical implementation. All projects target .NET 9.

## First feature: vehicle registration and retrieval

The dependency flow is controller → service interface → Application service → repository interface → Infrastructure repository → EF Core DbContext → SQL Server. Controllers and services do not use DbContext. Request/response DTOs live in Contracts; Domain entities are not returned from the API.

Vehicle fields are a generated UUID and a registration number. Registration numbers are required, trimmed, uppercased invariantly, and limited to 32 UTF-16 characters after normalization. No country-specific format, vehicle category, capacity, driver, or marketplace policy is imposed. Normalized numbers are unique; the SQL Server unique index also resolves concurrent registration attempts.

| InternalApi route | Behavior |
|---|---|
| `POST /api/vehicles` | Body: `{"registrationNumber":"abc-123"}`. Returns `201` with `{ "id": "<uuid>", "registrationNumber": "ABC-123" }` and a retrieval Location header; `400` validation problem for invalid input; `409` problem for duplicate registration. |
| `GET /api/vehicles` | Admin or Dispatcher only. Returns the registered fleet as vehicle DTOs. |
| `GET /api/vehicles/{id}` | Returns the vehicle DTO with `200`, or `404` when absent. The route requires a UUID. |

Existing weather endpoints remain. Delivery lifecycle, driver accounts, Hangfire processing, Android, and marketplace behavior are not implemented. MVC has a Fleet Registry page, an Admin-only vehicle registration form, and a Dispatcher Fleet Planner.

## Staff access

The MVC app uses ASP.NET Core Identity cookies. Admin and Dispatcher can view the Fleet Registry; Admin can register vehicles and create staff accounts; Dispatcher can use the Fleet Planner. There is no public self-registration. The Internal API uses Identity bearer tokens: `POST /api/auth/login` accepts staff email/password and returns a token, and `GET /api/auth/me` returns the authenticated staff member and roles. Its vehicle POST requires Admin; list and GET-by-ID allow Admin or Dispatcher; Driver authentication is not included.

Apply migrations explicitly before running either host. For a new development database, apply both migrations with the command below. The initial Admin email is fixed as `admin@dalan.local`. Supply `DALAN_ADMIN_PASSWORD` in the Web process environment for its first startup. The app creates Admin and Dispatcher roles and the initial Admin account, but does not migrate the database or reset an existing account's password. Remove the environment variable after creation. No password is committed. Further staff accounts can be created by an Admin in the MVC app.

## Fleet optimization and Valhalla

Set `Valhalla:BaseUrl` (or `Valhalla__BaseUrl`) to the base URL of a permitted Valhalla instance. This enables the Dispatcher Fleet Planner and `POST /api/fleet/optimize`. Without it, the API returns `503` and the MVC planner reports that routing is unavailable. The optimizer uses Valhalla's automobile time-distance matrix to assign unassigned jobs to registered vehicles and improve the order of jobs already assigned to a vehicle. Every job has pickup and drop-off coordinates; pickup always precedes drop-off. It minimizes estimated total driving time using a heuristic, so it does not guarantee a global optimum.

The optimizer accepts 1–8 vehicles and 1–15 jobs. Vehicle IDs must already exist in the registry. Each vehicle needs a starting coordinate. A job's optional `assignedVehicleId` keeps it on that vehicle; omitting it lets the planner assign it. The response lists each vehicle's ordered stops, estimated road distance/time, and jobs for which no feasible road route was found. The calculation is a preview only: jobs and plans are not stored or dispatched. Capacity, time windows, service time, traffic departure time, and return-to-depot are not modeled. Manual coordinates are used; no map, geocoder, Google Places, or Valhalla server is bundled.

Example request body for `POST /api/fleet/optimize` (use a Dispatcher bearer token):

```json
{
  "vehicles": [{ "vehicleId": "<registered-vehicle-uuid>", "start": { "latitude": 14.6, "longitude": 121.0 } }],
  "jobs": [{ "jobId": "JOB-1", "pickup": { "latitude": 14.61, "longitude": 121.01 }, "dropoff": { "latitude": 14.62, "longitude": 121.02 } }]
}
```

An application-level `IRoutingService` and a Valhalla route adapter also return point-to-point road distance, duration, and encoded polyline6 geometry for future map display. No public route geometry endpoint exists yet. Choose the provider host and vehicle costing rules before deployment.

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

After selecting the intended development database, apply migrations explicitly:

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

The current unit suite includes vehicle and Valhalla adapter tests. SQL Server/API integration tests require the database service described above.

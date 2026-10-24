# Dalan API contract

This sheet documents the routes implemented in the current source tree. It is a
reference for consumers, not a proposal for new endpoints or behavior.

## Hosts and conventions

Two ASP.NET Core API hosts exist:

| Host | Implemented routes |
| --- | --- |
| `Dalan.InternalApi` | Authentication, vehicles, fleet optimization, and the template weather route |
| `Dalan.ExternalApi` | The template weather route only |

The Internal API development launch profile uses `https://localhost:7261` (and
`http://localhost:5072`). Request and response JSON uses ASP.NET Core's default
camel-case property names. Protected Internal API routes expect an Identity
bearer token in `Authorization: Bearer <accessToken>`.

`401 Unauthorized` means the request has no valid bearer identity. `403 Forbidden`
means the caller is authenticated but does not have a required role.

## Authorization matrix

| Route | Anonymous | Admin | Dispatcher |
| --- | :---: | :---: | :---: |
| `POST /api/auth/login` | Allowed | Allowed | Allowed |
| `GET /api/auth/me` | 401 | Allowed | Allowed |
| `GET /api/vehicles` | 401 | Allowed | Allowed |
| `POST /api/vehicles` | 401 | Allowed | 403 |
| `GET /api/vehicles/{id}` | 401 | Allowed | Allowed |
| `POST /api/fleet/optimize` | 401 | 403 | Allowed |
| `GET /WeatherForecast` | Allowed | Allowed | Allowed |

Only `Admin` and `Dispatcher` accounts can log in through the Internal API.
There is no Driver API authentication flow.

## Authentication

### `POST /api/auth/login`

Authenticates a staff account and issues an ASP.NET Core Identity bearer-token
response.

**Authorization:** anonymous

**Request body**

```json
{
  "email": "dispatcher@example.test",
  "password": "the-account-password"
}
```

| Field | Type | Required | Validation |
| --- | --- | :---: | --- |
| `email` | string | Yes | Must be a valid email address. |
| `password` | string | Yes | Required. |

**Responses**

| Status | Body | Meaning |
| --- | --- | --- |
| 200 | Identity bearer-token payload | Valid Admin or Dispatcher credentials. Consumers use `accessToken` as the bearer token. |
| 400 | `ValidationProblemDetails` | Missing or malformed request fields, through `[ApiController]` model validation. |
| 401 | Empty response | Unknown account, invalid password, locked-out/unsuccessful sign-in, or an account without Admin or Dispatcher. |

The precise token lifetime and any refresh-token fields are framework-generated
by the configured Identity bearer-token handler; clients must treat fields other
than `accessToken` as handler-defined.

### `GET /api/auth/me`

Returns the currently authenticated staff profile.

**Authorization:** `Admin` or `Dispatcher`

**Request body:** none

**200 response**

```json
{
  "id": "identity-user-id",
  "email": "dispatcher@example.test",
  "roles": ["Dispatcher"]
}
```

| Field | Type | Notes |
| --- | --- | --- |
| `id` | string | ASP.NET Core Identity user ID. |
| `email` | string | Authenticated user's email. |
| `roles` | string[] | Role names sorted alphabetically. |

**Other responses:** `401` when unauthenticated or when the resolved Identity
user has no email; `403` for another authenticated role.

## Vehicles

### `GET /api/vehicles`

Lists every registered vehicle.

**Authorization:** `Admin` or `Dispatcher`  
**Request body:** none

**200 response**

```json
[
  {
    "id": "e4f4e608-9512-499c-8c95-2a4190b29b89",
    "registrationNumber": "ABC-123"
  }
]
```

The list may be empty. The current contract does not promise an ordering.

**Other responses:** `401` when unauthenticated; `403` for an authenticated role
other than Admin or Dispatcher.

### `POST /api/vehicles`

Registers a vehicle.

**Authorization:** `Admin`

**Request body**

```json
{
  "registrationNumber": "abc-123"
}
```

| Field | Type | Required | Normalization and validation |
| --- | --- | :---: | --- |
| `registrationNumber` | string | Yes | Trimmed, converted to uppercase invariantly, and must contain 1–32 UTF-16 characters after trimming. |

**201 response**

```json
{
  "id": "e4f4e608-9512-499c-8c95-2a4190b29b89",
  "registrationNumber": "ABC-123"
}
```

The response includes a `Location` header pointing to
`/api/vehicles/{id}`.

**Error responses**

| Status | Body | When |
| --- | --- | --- |
| 400 | `ValidationProblemDetails` | Missing `registrationNumber`, or blank/over-32-character value after normalization. |
| 401 | Empty response | No valid bearer identity. |
| 403 | Empty response | Authenticated caller is not an Admin. |
| 409 | `ProblemDetails` | A vehicle already has the same normalized registration number. Title: `Duplicate vehicle registration`. |

### `GET /api/vehicles/{id}`

Gets one vehicle by generated UUID.

**Authorization:** `Admin` or `Dispatcher`  
**Path parameter:** `id` is a GUID  
**Request body:** none

**200 response:** the same vehicle object used by the list and create routes.

**Other responses:** `401` when unauthenticated; `403` for another role; `404`
when the GUID is valid but not registered. A non-GUID path value does not match
this route and is handled by ASP.NET Core routing.

## Fleet optimization

### `POST /api/fleet/optimize`

Builds a non-persisted route-plan preview using the configured Valhalla routing
provider. All `vehicleId` values must identify registered vehicles.

**Authorization:** `Dispatcher` only

**Request body**

```json
{
  "vehicles": [
    {
      "vehicleId": "e4f4e608-9512-499c-8c95-2a4190b29b89",
      "start": { "latitude": 14.6, "longitude": 121.0 }
    }
  ],
  "jobs": [
    {
      "jobId": "JOB-1",
      "pickup": { "latitude": 14.61, "longitude": 121.01 },
      "dropoff": { "latitude": 14.62, "longitude": 121.02 },
      "assignedVehicleId": null
    }
  ]
}
```

| Field | Type | Required | Constraints |
| --- | --- | :---: | --- |
| `vehicles` | array | Yes | 1–8 entries; each vehicle ID must be a registered, non-empty GUID and must be unique. |
| `vehicles[].start` | coordinate | Yes | Valid latitude/longitude coordinate. |
| `jobs` | array | Yes | 1–15 entries. |
| `jobs[].jobId` | string | Yes | Nonblank and unique, using ordinal comparison. |
| `jobs[].pickup` | coordinate | Yes | Valid latitude/longitude coordinate. |
| `jobs[].dropoff` | coordinate | Yes | Valid latitude/longitude coordinate. |
| `jobs[].assignedVehicleId` | GUID or null | No | When supplied, must name a vehicle in `vehicles`; it fixes that job to the specified vehicle. |

A coordinate is an object with numeric `latitude` and `longitude` fields. The
application validates coordinate validity before calling the routing provider.

**200 response**

```json
{
  "routes": [
    {
      "vehicleId": "e4f4e608-9512-499c-8c95-2a4190b29b89",
      "stops": [
        {
          "jobId": "JOB-1",
          "kind": "Pickup",
          "location": { "latitude": 14.61, "longitude": 121.01 }
        },
        {
          "jobId": "JOB-1",
          "kind": "Dropoff",
          "location": { "latitude": 14.62, "longitude": 121.02 }
        }
      ],
      "distanceMeters": 1200,
      "durationSeconds": 300
    }
  ],
  "unassignedJobIds": []
}
```

The response contains one route per request vehicle, including empty routes.
`kind` is either `Pickup` or `Dropoff`; pickup precedes dropoff for each job.
`distanceMeters` and `durationSeconds` are routing-provider estimates. The plan
is not saved or dispatched.

**Error responses**

| Status | Body | When |
| --- | --- | --- |
| 400 | `ValidationProblemDetails` | Missing vehicle/job lists or coordinates; an unregistered vehicle; invalid counts, IDs, duplicate IDs, coordinates, or fixed assignment. |
| 401 | Empty response | No valid bearer identity. |
| 403 | Empty response | Authenticated caller is not a Dispatcher. |
| 502 | `ProblemDetails` | Valhalla request fails or returns invalid data. |
| 503 | `ProblemDetails` | `Valhalla:BaseUrl` is not configured. Title: `Routing is unavailable`. |
| 504 | `ProblemDetails` | The routing provider times out. |

## Template weather route

Both `Dalan.InternalApi` and `Dalan.ExternalApi` expose this development-template
route independently.

### `GET /WeatherForecast`

**Authorization:** anonymous  
**Request body:** none

Returns `200 OK` with five generated forecast objects:

```json
[
  {
    "date": "2026-10-25",
    "temperatureC": 21,
    "temperatureF": 69,
    "summary": "Mild"
  }
]
```

| Field | Type | Notes |
| --- | --- | --- |
| `date` | `YYYY-MM-DD` | A date within the next five local days. |
| `temperatureC` | integer | Random integer from -20 through 54. |
| `temperatureF` | integer | Computed from `temperatureC` by the implementation. |
| `summary` | string | One of the built-in sample weather descriptions. |

This is scaffold/template behavior, not a real weather integration or an
authorization-protected operational API.

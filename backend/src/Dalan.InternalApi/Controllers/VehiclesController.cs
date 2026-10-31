using Dalan.Application.Vehicles;
using Dalan.Contracts.Vehicles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Route("api/vehicles")]
public sealed class VehiclesController(IVehicleService vehicleService) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin,Dispatcher")]
    [ProducesResponseType<IReadOnlyList<VehicleResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<VehicleResponse>>> List(
    CancellationToken cancellationToken)
    {
        var vehicles = await vehicleService.ListAsync(cancellationToken);

        return Ok(vehicles.Select(ToResponse).ToArray());
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<VehicleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VehicleResponse>> Register(
        RegisterVehicleRequest request, CancellationToken cancellationToken)
    {
        var result = await vehicleService.RegisterAsync(
            new RegisterVehicleCommand(request.RegistrationNumber, request.Type,
                request.PayloadCapacityKg, request.SeatCapacity, request.NextMaintenanceDate),
            cancellationToken);

        if (result.Status == VehicleRegistrationStatus.Invalid)
        {
            ModelState.AddModelError(nameof(request.RegistrationNumber), result.Error!);
            return ValidationProblem(ModelState);
        }

        if (result.Status == VehicleRegistrationStatus.Duplicate)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Duplicate vehicle registration", detail: result.Error);
        }

        var response = ToResponse(result.Vehicle!);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Dispatcher")]
    [ProducesResponseType<VehicleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VehicleResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await vehicleService.GetByIdAsync(id, cancellationToken);
        return vehicle is null ? NotFound() : Ok(ToResponse(vehicle));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<VehicleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VehicleResponse>> Update(
        Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken)
    {
        var result = await vehicleService.UpdateAsync(id,
            new UpdateVehicleCommand(request.Type, request.PayloadCapacityKg,
                request.SeatCapacity, request.Availability ?? string.Empty,
                request.NextMaintenanceDate), cancellationToken);
        return MutationResponse(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<VehicleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VehicleResponse>> Deactivate(
        Guid id, CancellationToken cancellationToken)
        => MutationResponse(await vehicleService.DeactivateAsync(id, cancellationToken));

    private ActionResult<VehicleResponse> MutationResponse(VehicleMutationResult result)
        => result.Status switch
        {
            VehicleMutationStatus.Updated => Ok(ToResponse(result.Vehicle!)),
            VehicleMutationStatus.NotFound => NotFound(),
            VehicleMutationStatus.Invalid => Invalid(result.Error!),
            VehicleMutationStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Vehicle cannot be changed", detail: result.Error),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

    private ActionResult<VehicleResponse> Invalid(string error)
    {
        ModelState.AddModelError("Vehicle", error);
        return ValidationProblem(ModelState);
    }

    private static VehicleResponse ToResponse(VehicleDetails vehicle)
        => new(vehicle.Id, vehicle.RegistrationNumber, vehicle.Type,
            vehicle.PayloadCapacityKg, vehicle.SeatCapacity,
            vehicle.Availability, vehicle.NextMaintenanceDate);
}

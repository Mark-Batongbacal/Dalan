using Dalan.Application.Vehicles;
using Dalan.Contracts.Vehicles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Route("api/vehicles")]
public sealed class VehiclesController(IVehicleService vehicleService) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<VehicleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VehicleResponse>> Register(
        RegisterVehicleRequest request, CancellationToken cancellationToken)
    {
        var result = await vehicleService.RegisterAsync(
            new RegisterVehicleCommand(request.RegistrationNumber), cancellationToken);

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

    private static VehicleResponse ToResponse(VehicleDetails vehicle)
        => new(vehicle.Id, vehicle.RegistrationNumber);
}

using Dalan.Application.Drivers;
using Dalan.Contracts.Drivers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Route("api/drivers")]
[Authorize(Roles = "Admin")]
public sealed class DriversController(IDriverService drivers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DriverResponse>>> List(CancellationToken cancellationToken)
        => Ok((await drivers.ListAsync(cancellationToken)).Select(ToResponse).ToArray());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DriverResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var driver = await drivers.GetByIdAsync(id, cancellationToken);
        return driver is null ? NotFound() : Ok(ToResponse(driver));
    }

    [HttpPost]
    public async Task<ActionResult<DriverResponse>> Register(RegisterDriverRequest request,
        CancellationToken cancellationToken)
    {
        var result = await drivers.RegisterAsync(new RegisterDriverCommand(
            request.Name, request.Phone, request.LicenseNumber,
            request.LicenseExpiry ?? default), cancellationToken);
        if (result.Status == DriverMutationStatus.Created)
        {
            var response = ToResponse(result.Driver!);
            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        return MutationResponse(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DriverResponse>> Update(Guid id, UpdateDriverRequest request,
        CancellationToken cancellationToken)
        => MutationResponse(await drivers.UpdateAsync(id, new UpdateDriverCommand(
            request.Name, request.Phone, request.LicenseNumber,
            request.LicenseExpiry ?? default, request.Availability ?? string.Empty,
            request.AssignedVehicleId), cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<DriverResponse>> Deactivate(Guid id,
        CancellationToken cancellationToken)
        => MutationResponse(await drivers.DeactivateAsync(id, cancellationToken));

    private ActionResult<DriverResponse> MutationResponse(DriverMutationResult result)
        => result.Status switch
        {
            DriverMutationStatus.Updated => Ok(ToResponse(result.Driver!)),
            DriverMutationStatus.NotFound => NotFound(),
            DriverMutationStatus.Invalid => Invalid(result.Error!),
            DriverMutationStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Driver cannot be changed", detail: result.Error),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

    private ActionResult<DriverResponse> Invalid(string error)
    {
        ModelState.AddModelError("Driver", error);
        return ValidationProblem(ModelState);
    }

    private static DriverResponse ToResponse(DriverDetails driver)
        => new(driver.Id, driver.Name, driver.Phone, driver.LicenseNumber,
            driver.LicenseExpiry, driver.Availability, driver.AssignedVehicleId);
}

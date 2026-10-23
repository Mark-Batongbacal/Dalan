using Dalan.Application.Vehicles;
using Dalan.Web.Identity;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles = IdentityRoles.FleetRegistryViewer)]
public sealed class VehiclesController(IVehicleService vehicleService) : Controller
{
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var vehicles = await vehicleService.ListAsync(cancellationToken);

        var model = new FleetRegistryViewModel(
            vehicles
                .Select(vehicle => new VehicleListItemViewModel(
                    vehicle.Id,
                    vehicle.RegistrationNumber))
                .ToArray());

        return View(model);
    }
}
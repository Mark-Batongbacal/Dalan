using Dalan.Application.Vehicles;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

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
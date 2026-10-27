using Dalan.Application.Vehicles;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Admin,Dispatcher")]
public sealed class VehiclesController(IVehicleService vehicleService) : Controller
{
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult Create() => View(new CreateVehicleViewModel());

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateVehicleViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await vehicleService.RegisterAsync(
            new RegisterVehicleCommand(model.RegistrationNumber), cancellationToken);
        if (result.Status != VehicleRegistrationStatus.Registered)
        {
            ModelState.AddModelError(nameof(model.RegistrationNumber), result.Error!);
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

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

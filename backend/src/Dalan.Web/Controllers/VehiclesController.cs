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
            new RegisterVehicleCommand(model.RegistrationNumber, model.Type,
                model.PayloadCapacityKg, model.SeatCapacity, model.NextMaintenanceDate),
            cancellationToken);
        if (result.Status != VehicleRegistrationStatus.Registered)
        {
            ModelState.AddModelError(nameof(model.RegistrationNumber), result.Error!);
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await vehicleService.GetByIdAsync(id, cancellationToken);
        return vehicle is null ? NotFound() : View(ToEditModel(vehicle));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditVehicleViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        var result = await vehicleService.UpdateAsync(id,
            new UpdateVehicleCommand(model.Type, model.PayloadCapacityKg,
                model.SeatCapacity, model.Availability, model.NextMaintenanceDate),
            cancellationToken);
        if (result.Status == VehicleMutationStatus.NotFound) return NotFound();
        if (result.Status != VehicleMutationStatus.Updated)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(model);
        }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await vehicleService.DeactivateAsync(id, cancellationToken);
        if (result.Status == VehicleMutationStatus.NotFound) return NotFound();
        if (result.Status != VehicleMutationStatus.Updated)
        {
            TempData["VehicleError"] = result.Error;
            return RedirectToAction(nameof(Edit), new { id });
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
                    vehicle.RegistrationNumber,
                    vehicle.Type,
                    vehicle.PayloadCapacityKg,
                    vehicle.SeatCapacity,
                    vehicle.Availability,
                    vehicle.NextMaintenanceDate))
                .ToArray());

        return View(model);
    }

    private static EditVehicleViewModel ToEditModel(VehicleDetails vehicle)
        => new()
        {
            Id = vehicle.Id,
            RegistrationNumber = vehicle.RegistrationNumber,
            Type = vehicle.Type,
            PayloadCapacityKg = vehicle.PayloadCapacityKg,
            SeatCapacity = vehicle.SeatCapacity,
            Availability = vehicle.Availability,
            NextMaintenanceDate = vehicle.NextMaintenanceDate
        };
}

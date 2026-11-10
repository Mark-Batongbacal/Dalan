using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class DriversController(
    IDriverService drivers, IVehicleService vehicles) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var registrations = (await vehicles.ListAsync(cancellationToken))
            .ToDictionary(vehicle => vehicle.Id, vehicle => vehicle.RegistrationNumber);
        var items = (await drivers.ListAsync(cancellationToken))
            .Select(driver => new DriverListItemViewModel(
                driver.Id, driver.Name, driver.Phone, driver.LicenseNumber,
                driver.LicenseExpiry, driver.Availability,
                driver.AssignedVehicleId is Guid id && registrations.TryGetValue(id, out var registration)
                    ? registration : null))
            .ToArray();
        return View(new DriverRegistryViewModel(items));
    }

    [HttpGet]
    public IActionResult Create() => View(new DriverFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DriverFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await drivers.RegisterAsync(new RegisterDriverCommand(
            model.Name, model.Phone, model.LicenseNumber, model.LicenseExpiry!.Value),
            cancellationToken);
        if (result.Status != DriverMutationStatus.Created)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(model);
        }
        return RedirectToAction(nameof(Edit), new { id = result.Driver!.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var driver = await drivers.GetByIdAsync(id, cancellationToken);
        if (driver is null) return NotFound();
        var model = ToForm(driver);
        await PopulateVehicles(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, DriverFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        await PopulateVehicles(model, cancellationToken);
        if (!ModelState.IsValid) return View(model);
        var result = await drivers.UpdateAsync(id, new UpdateDriverCommand(
            model.Name, model.Phone, model.LicenseNumber, model.LicenseExpiry!.Value,
            model.Availability, model.AssignedVehicleId), cancellationToken);
        if (result.Status == DriverMutationStatus.NotFound) return NotFound();
        if (result.Status != DriverMutationStatus.Updated)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(model);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await drivers.DeactivateAsync(id, cancellationToken);
        return result.Status == DriverMutationStatus.NotFound
            ? NotFound() : RedirectToAction(nameof(Index));
    }

    private async Task PopulateVehicles(DriverFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Vehicles = (await vehicles.ListAsync(cancellationToken))
            .Where(vehicle => vehicle.Availability == "Available"
                || vehicle.Id == model.AssignedVehicleId)
            .Select(vehicle => new SelectListItem(
                vehicle.RegistrationNumber, vehicle.Id.ToString(),
                vehicle.Id == model.AssignedVehicleId))
            .ToArray();
    }

    private static DriverFormViewModel ToForm(DriverDetails driver)
        => new()
        {
            Id = driver.Id,
            Name = driver.Name,
            Phone = driver.Phone,
            LicenseNumber = driver.LicenseNumber,
            LicenseExpiry = driver.LicenseExpiry,
            Availability = driver.Availability,
            AssignedVehicleId = driver.AssignedVehicleId
        };
}

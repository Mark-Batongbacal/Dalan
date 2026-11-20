using Dalan.Application.DriverExecution;
using Dalan.Application.Deliveries;
using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Dispatcher")]
public sealed class DeliveriesController(
    IDeliveryService deliveries,
    IDriverService drivers,
    IVehicleService vehicles, TimeProvider clock) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ProofPhoto(Guid id, [FromServices] IDriverTasks tasks,
        CancellationToken cancellationToken)
    {
        var photo = await tasks.GetProofPhotoAsync(id, cancellationToken);
        if (photo is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(photo, "image/jpeg");
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await deliveries.ListAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Details(
        Guid id, CancellationToken cancellationToken)
    {
        var delivery = await deliveries.GetByIdAsync(id, cancellationToken);
        if (delivery is null) return NotFound();

        return View(new DeliveryDetailsViewModel(
            delivery,
            await drivers.ListAsync(cancellationToken),
            await vehicles.ListAsync(cancellationToken)));
    }

    [HttpGet]
    public IActionResult Create()
    {
        var now = clock.GetUtcNow();
        var nextSlot = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute / 30 * 30, 0, TimeSpan.Zero).AddMinutes(30);
        return View(new CreateDeliveryViewModel { PickupAddress = "Warehouse", ExpectedDeliveryAtUtc = nextSlot });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateDeliveryViewModel model, CancellationToken cancellationToken, string? next = null)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await deliveries.CreateAsync(new CreateDeliveryCommand(
            model.RecipientName,
            model.PickupAddress,
            model.PickupLatitude!.Value,
            model.PickupLongitude!.Value,
            model.DropoffAddress,
            model.DropoffLatitude!.Value,
            model.DropoffLongitude!.Value,
            model.PayloadWeightKg,
            model.ExpectedDeliveryAtUtc), cancellationToken);

        if (result.Status != DeliveryMutationStatus.Created)
        {
            ModelState.AddModelError(string.Empty,
                result.Error ?? "The delivery could not be created.");
            return View(model);
        }

        if (next == "another") { TempData["WorkflowNotice"] = "Delivery created. Add your next delivery."; return RedirectToAction(nameof(Create)); }
        return RedirectToAction(nameof(Details), new { id = result.Delivery!.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        Guid id, Guid driverId, Guid vehicleId,
        CancellationToken cancellationToken, string? next = null)
    {
        if (driverId == Guid.Empty || vehicleId == Guid.Empty)
        {
            TempData["DeliveryError"] = "Choose a driver and vehicle.";
            return next == "workflow" ? RedirectToAction("Assignments", "DeliveryWorkflow") : RedirectToAction(nameof(Details), new { id });
        }

        var result = await deliveries.AssignAsync(
            id, new AssignDeliveryCommand(driverId, vehicleId),
            cancellationToken);

        if (result.Status == DeliveryMutationStatus.NotFound)
            return NotFound();

        if (result.Status != DeliveryMutationStatus.Updated)
            TempData["DeliveryError"] =
                result.Error ?? "The assignment could not be saved.";

        return next == "workflow" ? RedirectToAction("Assignments", "DeliveryWorkflow") : RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
    Guid id, CancellationToken cancellationToken)
    {
        var result = await deliveries.CancelAsync(id, cancellationToken);

        if (result.Status == DeliveryMutationStatus.NotFound)
            return NotFound();

        if (result.Status != DeliveryMutationStatus.Updated)
            TempData["DeliveryError"] =
                result.Error ?? "The delivery could not be cancelled.";

        return RedirectToAction(nameof(Details), new { id });
    }
}

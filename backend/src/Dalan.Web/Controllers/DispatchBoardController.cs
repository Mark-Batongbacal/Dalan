using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Application.Drivers;
using Dalan.Application.Routing;
using Dalan.Application.Vehicles;
using Dalan.Domain.Dispatch;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Dispatcher")]
public sealed class DispatchBoardController(
    IDeliveryService deliveries,
    IDriverService drivers,
    IVehicleService vehicles,
    IDispatchPlanReader planReader,
    IServiceProvider services) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new DispatchBoardViewModel();
        await PopulateChoices(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        DispatchBoardViewModel model, CancellationToken cancellationToken)
    {
        await PopulateChoices(model, cancellationToken);

        var selectedVehicles = model.Vehicles.Where(x => x.Selected).ToArray();
        var selectedDeliveries = model.Deliveries.Where(x => x.Selected).ToArray();

        if (selectedVehicles.Length is < 1 or > 8 ||
            selectedDeliveries.Length is < 1 or > 15)
        {
            model.Error = "Select 1–8 vehicles and 1–15 deliveries.";
            return View(model);
        }

        if (selectedVehicles.Any(x =>
                x.StartLatitude is null || x.StartLongitude is null))
        {
            model.Error = "Enter a start coordinate for every selected vehicle.";
            return View(model);
        }

        var dispatch = services.GetService<IDispatchService>();
        if (dispatch is null)
        {
            model.Error = "Routing is unavailable. Configure a Valhalla base URL.";
            return View(model);
        }

        try
        {
            model.Preview = await dispatch.PreviewAsync(
                new DispatchPreviewRequest(
                    selectedVehicles.Select(x => new DispatchVehicleStart(
                        x.Id,
                        new GeoPoint(
                            x.StartLatitude!.Value,
                            x.StartLongitude!.Value))).ToArray(),
                    selectedDeliveries.Select(x => x.Id).ToArray()),
                cancellationToken);
        }
        catch (ArgumentException error) { model.Error = error.Message; }
        catch (KeyNotFoundException) { model.Error = "A selected delivery or vehicle no longer exists. Reload and try again."; }
        catch (InvalidOperationException error) { model.Error = error.Message; }
        catch (HttpRequestException) { model.Error = "The routing provider failed."; }
        catch (InvalidDataException) { model.Error = "The routing provider returned invalid data."; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            model.Error = "The routing provider timed out.";
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
    DispatchBoardViewModel board, CancellationToken cancellationToken)
    {
        // Re-preview from current records. Do not trust a posted preview result.
        await PopulateChoices(board, cancellationToken);

        var selectedVehicles = board.Vehicles.Where(x => x.Selected).ToArray();
        var selectedDeliveries = board.Deliveries.Where(x => x.Selected).ToArray();

        if (selectedVehicles.Length is < 1 or > 8 ||
            selectedDeliveries.Length is < 1 or > 15 ||
            selectedVehicles.Any(x =>
                x.StartLatitude is null || x.StartLongitude is null))
        {
            board.Error = "Select 1–8 vehicles and 1–15 deliveries, with a start for each vehicle.";
            return View("Index", board);
        }

        var dispatch = services.GetService<IDispatchService>();
        if (dispatch is null)
        {
            board.Error = "Routing is unavailable. Configure a Valhalla base URL.";
            return View("Index", board);
        }

        try
        {
            var preview = await dispatch.PreviewAsync(
                new DispatchPreviewRequest(
                    selectedVehicles.Select(x => new DispatchVehicleStart(
                        x.Id,
                        new GeoPoint(
                            x.StartLatitude!.Value,
                            x.StartLongitude!.Value))).ToArray(),
                    selectedDeliveries.Select(x => x.Id).ToArray()),
                cancellationToken);

            if (preview.UnassignedDeliveryIds.Count > 0)
            {
                board.Preview = preview;
                board.Error =
                    "Some selected deliveries could not be assigned. Deselect them and preview again before confirming.";
                return View("Index", board);
            }

            var assignedIds = preview.Routes
                .SelectMany(x => x.Stops)
                .Select(x => x.DeliveryId)
                .ToHashSet();

            if (assignedIds.Count == 0)
            {
                board.Preview = preview;
                board.Error = "No deliveries could be assigned. Review the preview before confirming.";
                return View("Index", board);
            }

            var review = new DispatchReviewViewModel
            {
                Vehicles = selectedVehicles.Select(x => new DispatchReviewVehicle
                {
                    VehicleId = x.Id,
                    StartLatitude = x.StartLatitude!.Value,
                    StartLongitude = x.StartLongitude!.Value
                }).ToList(),
                Stops = preview.Routes
                    .SelectMany(route => route.Stops.Select((stop, order) =>
                        new DispatchReviewStop
                        {
                            DeliveryId = stop.DeliveryId,
                            Kind = stop.Kind.ToString(),
                            VehicleId = route.VehicleId,
                            Order = order
                        })).ToList(),
                Versions = preview.DeliveryVersions
                    .Where(x => assignedIds.Contains(x.DeliveryId))
                    .Select(x => new DispatchReviewVersion
                    {
                        DeliveryId = x.DeliveryId,
                        RowVersion = Convert.ToBase64String(x.RowVersion)
                    }).ToList()
            };

            await PopulateReviewNames(review, cancellationToken);
            return View(review);
        }
        catch (ArgumentException error) { board.Error = error.Message; }
        catch (KeyNotFoundException) { board.Error = "A selected delivery or vehicle no longer exists."; }
        catch (InvalidOperationException error) { board.Error = error.Message; }
        catch (HttpRequestException) { board.Error = "The routing provider failed."; }
        catch (InvalidDataException) { board.Error = "The routing provider returned invalid data."; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            board.Error = "The routing provider timed out.";
        }

        return View("Index", board);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(
        DispatchReviewViewModel model, CancellationToken cancellationToken)
    {
        async Task<IActionResult> ReviewError(string message)
        {
            model.Error = message;
            await PopulateReviewNames(model, cancellationToken);
            return View("Review", model);
        }

        if (model.Vehicles is null || model.Stops is null ||
            model.Versions is null ||
            model.Vehicles.Count is < 1 or > 8 ||
            model.Stops.Count is < 2 or > 30 ||
            model.Versions.Count is < 1 or > 15)
        {
            return await ReviewError("The reviewed plan is incomplete.");
        }

        var vehicleIds = model.Vehicles.Select(x => x.VehicleId).ToArray();
        if (vehicleIds.Any(x => x == Guid.Empty) ||
            vehicleIds.Distinct().Count() != vehicleIds.Length ||
            model.Stops.Any(x =>
                x.DeliveryId == Guid.Empty ||
                !vehicleIds.Contains(x.VehicleId) ||
                x.Order < 0 ||
                x.Kind is not ("Pickup" or "Dropoff")) ||
            model.Stops.GroupBy(x => x.VehicleId)
                .Any(group => group.Select(x => x.Order).Distinct().Count()
                    != group.Count()))
        {
            return await ReviewError(
                "Use unique vehicles and stop positions. Each stop must have a valid vehicle and kind.");
        }

        var dispatch = services.GetService<IDispatchService>();
        if (dispatch is null)
            return await ReviewError("Routing is unavailable. Configure a Valhalla base URL.");

        try
        {
            var starts = model.Vehicles.Select(x => new DispatchVehicleStart(
                x.VehicleId,
                new GeoPoint(x.StartLatitude, x.StartLongitude))).ToArray();

            var routes = model.Vehicles.Select(vehicle => new DispatchReviewedRoute(
                vehicle.VehicleId,
                model.Stops
                    .Where(x => x.VehicleId == vehicle.VehicleId)
                    .OrderBy(x => x.Order)
                    .Select(x => new DispatchReviewedStop(
                        x.DeliveryId,
                        Enum.Parse<DispatchStopKind>(x.Kind)))
                    .ToArray())).ToArray();

            var versions = model.Versions.Select(x => new DispatchDeliveryVersion(
                x.DeliveryId,
                Convert.FromBase64String(x.RowVersion))).ToArray();

            var plan = await dispatch.ConfirmAsync(
                new DispatchConfirmRequest(starts, routes, versions),
                cancellationToken);

            if (plan is null)
                return await ReviewError(
                    "The preview is stale or a delivery has already been dispatched. Start a new preview.");

            return RedirectToAction(nameof(Plan), new { id = plan.Id });
        }
        catch (FormatException)
        {
            return await ReviewError("The preview version is invalid. Start a new preview.");
        }
        catch (ArgumentException error)
        {
            return await ReviewError(error.Message);
        }
        catch (KeyNotFoundException)
        {
            return await ReviewError("A selected delivery or vehicle no longer exists.");
        }
        catch (InvalidOperationException error)
        {
            return await ReviewError(error.Message);
        }
        catch (HttpRequestException)
        {
            return await ReviewError("The routing provider failed.");
        }
        catch (InvalidDataException)
        {
            return await ReviewError("The routing provider returned invalid data.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await ReviewError("The routing provider timed out.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Drivers(CancellationToken cancellationToken)
    {
        var registrations = (await vehicles.ListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.RegistrationNumber);

        var rows = (await drivers.ListAsync(cancellationToken))
            .Select(driver => new DispatchDriverRow(
                driver.Name,
                driver.Availability,
                driver.LicenseExpiry,
                driver.AssignedVehicleId is Guid vehicleId &&
                    registrations.TryGetValue(vehicleId, out var registration)
                        ? registration
                        : null))
            .ToArray();

        return View(new DispatchDriverRosterViewModel(rows));
    }

    [HttpGet]
    public async Task<IActionResult> Plan(
        Guid id, CancellationToken cancellationToken)
    {
        var plan = await planReader.GetByIdAsync(id, cancellationToken);
        if (plan is null) return NotFound();

        return View(new DispatchPlanPageViewModel(
            plan,
            (await deliveries.ListAsync(cancellationToken)).ToDictionary(x => x.Id),
            (await drivers.ListAsync(cancellationToken)).ToDictionary(x => x.Id, x => x.Name),
            (await vehicles.ListAsync(cancellationToken))
                .ToDictionary(x => x.Id, x => x.RegistrationNumber)));
    }

    private async Task PopulateReviewNames(
        DispatchReviewViewModel model, CancellationToken cancellationToken)
    {
        model.VehicleNames = (await vehicles.ListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.RegistrationNumber);

        model.DeliveryNames = (await deliveries.ListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.RecipientName);
    }

    private async Task PopulateChoices(
        DispatchBoardViewModel model, CancellationToken cancellationToken)
    {
        var submittedVehicles = (model.Vehicles ?? [])
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First());

        var submittedDeliveries = (model.Deliveries ?? [])
            .Where(x => x.Selected)
            .Select(x => x.Id)
            .ToHashSet();

        var availableVehicles = await vehicles.ListAsync(cancellationToken);
        model.Vehicles = availableVehicles
            .Where(x => x.Availability == "Available")
            .Select(x =>
            {
                submittedVehicles.TryGetValue(x.Id, out var submitted);
                return new DispatchVehicleInput
                {
                    Id = x.Id,
                    RegistrationNumber = x.RegistrationNumber,
                    Selected = submitted?.Selected ?? false,
                    StartLatitude = submitted?.StartLatitude,
                    StartLongitude = submitted?.StartLongitude
                };
            }).ToList();

        var currentDeliveries = await deliveries.ListAsync(cancellationToken);
        model.Deliveries = currentDeliveries
            .Where(x => x.Status.ToString() is "Pending" or "Assigned")
            .Select(x => new DispatchDeliveryInput
            {
                Id = x.Id,
                RecipientName = x.RecipientName,
                PickupAddress = x.PickupAddress,
                DropoffAddress = x.DropoffAddress,
                Status = x.Status.ToString(),
                Selected = submittedDeliveries.Contains(x.Id)
            }).ToList();
    }
}

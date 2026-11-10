using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Dalan.Application.Vehicles;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Dispatcher")]
public sealed class FleetPlannerController(
    IVehicleService vehicleService, IServiceProvider services) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new FleetPlannerViewModel();
        await PopulateVehicles(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(FleetPlannerViewModel model, CancellationToken cancellationToken)
    {
        model.Jobs ??= [];
        await PopulateVehicles(model, cancellationToken);
        var selected = model.Vehicles.Where(vehicle => vehicle.Selected).ToArray();
        var jobs = model.Jobs.Where(job => !string.IsNullOrWhiteSpace(job.Id)).ToArray();
        if (selected.Length is < 1 or > 8 || jobs.Length is < 1 or > 15)
            model.Error = "Select 1–8 vehicles and enter 1–15 jobs.";
        else if (selected.Any(vehicle => vehicle.StartLatitude is null || vehicle.StartLongitude is null)
            || jobs.Any(job => job.PickupLatitude is null || job.PickupLongitude is null
                || job.DropoffLatitude is null || job.DropoffLongitude is null))
            model.Error = "Enter coordinates for every selected vehicle and job stop.";
        else
        {
            var optimizer = services.GetService<IFleetOptimizer>();
            if (optimizer is null)
                model.Error = "Routing is unavailable. Configure a Valhalla base URL.";
            else
            {
                try
                {
                    model.Result = await optimizer.OptimizeAsync(new FleetOptimizationRequest(
                        selected.Select(vehicle => new FleetVehicle(vehicle.Id,
                            new GeoPoint(vehicle.StartLatitude!.Value, vehicle.StartLongitude!.Value))).ToArray(),
                        jobs.Select(job => new FleetJob(job.Id!.Trim(),
                            new GeoPoint(job.PickupLatitude!.Value, job.PickupLongitude!.Value),
                            new GeoPoint(job.DropoffLatitude!.Value, job.DropoffLongitude!.Value),
                            job.AssignedVehicleId)).ToArray()), cancellationToken);
                }
                catch (ArgumentException error) { model.Error = error.Message; }
                catch (HttpRequestException) { model.Error = "The routing provider failed."; }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                { model.Error = "The routing provider timed out."; }
                catch (InvalidDataException) { model.Error = "The routing provider returned invalid data."; }
            }
        }
        if (model.Jobs.Count == 0) model.Jobs.Add(new PlannerJobInput());
        return View(model);
    }

    private async Task PopulateVehicles(FleetPlannerViewModel model, CancellationToken cancellationToken)
    {
        var submitted = model.Vehicles?
            .Where(vehicle => vehicle is not null)
            .GroupBy(vehicle => vehicle.Id)
            .ToDictionary(group => group.Key, group => group.First())
            ?? [];
        model.Vehicles = (await vehicleService.ListAsync(cancellationToken))
            .Where(vehicle => vehicle.Availability == "Available")
            .Select(vehicle =>
            {
                submitted.TryGetValue(vehicle.Id, out var previous);
                return new PlannerVehicleInput
                {
                    Id = vehicle.Id,
                    RegistrationNumber = vehicle.RegistrationNumber,
                    Selected = previous?.Selected ?? false,
                    StartLatitude = previous?.StartLatitude,
                    StartLongitude = previous?.StartLongitude
                };
            }).ToList();
    }
}

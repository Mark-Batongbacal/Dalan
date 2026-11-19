using Dalan.Application.Tracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

public sealed record FleetTrackingViewModel(FleetTrackingReport? Report, string? Error);

[Authorize(Roles = "Admin,Dispatcher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FleetTrackingController(IFleetTrackingService tracking, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (!ModelState.IsValid) return View(new FleetTrackingViewModel(null, "Choose valid UTC dates."));
        try { return View(new FleetTrackingViewModel(await tracking.ReportAsync(from ?? today, to ?? today, cancellationToken), null)); }
        catch (ArgumentException error) { return View(new FleetTrackingViewModel(null, error.Message)); }
    }

    [HttpGet]
    public async Task<IActionResult> Positions(CancellationToken cancellationToken)
    {
        return Json(await tracking.PositionsAsync(cancellationToken));
    }
}

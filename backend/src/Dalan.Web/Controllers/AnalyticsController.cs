using Dalan.Application.Analytics;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Admin,Dispatcher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AnalyticsController(IAnalyticsService analytics, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AnalyticsViewModel model, CancellationToken cancellationToken)
    {
        // Only bind filter fields; report data is always obtained from the service.
        model.Report = null;
        model.Error = null;
        if (model.From is null && model.To is null && ModelState.IsValid)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            model.From = today.AddDays(-29);
            model.To = today;
        }
        if (model.From is null) ModelState.AddModelError(nameof(model.From), "Choose a valid start date.");
        if (model.To is null) ModelState.AddModelError(nameof(model.To), "Choose a valid end date.");
        if (!ModelState.IsValid) return View(model);
        try
        {
            model.Report = await analytics.GetAsync(model.From!.Value, model.To!.Value, cancellationToken);
        }
        catch (ArgumentException error) { ModelState.AddModelError(string.Empty, error.Message); }
        catch (AnalyticsUnavailableException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            model.Error = "Analytics is temporarily unavailable. Try again shortly.";
        }
        return View(model);
    }
}

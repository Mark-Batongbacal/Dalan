using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Dalan.Web.Models;
using Dalan.Application.Analytics;

namespace Dalan.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index([FromServices] IAnalyticsService analytics,
        [FromServices] TimeProvider clock, CancellationToken cancellationToken)
    {
        if (User.IsInRole("Dispatcher") && !User.IsInRole("Admin"))
            return RedirectToAction("Index", "DispatchBoard");
        var model = new HomeDashboardViewModel();
        if (User.IsInRole("Admin") || User.IsInRole("Dispatcher"))
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            try { model.Report = await analytics.GetAsync(today.AddDays(-29), today, cancellationToken); }
            catch (AnalyticsUnavailableException error)
            {
                _logger.LogWarning(error, "Overview analytics unavailable");
                model.Error = "The overview is temporarily unavailable. Your workspace links are still available.";
            }
        }
        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Terms() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

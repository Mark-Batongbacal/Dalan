using System.Security.Claims;
using Dalan.Application.Dispatch;
using Dalan.Application.Routing;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles="Dispatcher")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class WarehouseController(IWarehousePreferenceService warehouses) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return Forbid();
        var saved = await warehouses.GetAsync(id, token);
        var model = saved is null ? new WarehousePreferenceViewModel() : new() { Name=saved.Name, Latitude=saved.Latitude, Longitude=saved.Longitude };
        model.ReturnToDispatch = Request.Query["workspace"] == "true";
        return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Current(CancellationToken token)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return Forbid();
        return Json(await warehouses.GetAsync(id, token));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(WarehousePreferenceViewModel model, CancellationToken token)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return Forbid();
        if (!ModelState.IsValid) return View(model);
        try { await warehouses.SaveAsync(id, new(model.Name, model.Latitude!.Value, model.Longitude!.Value), token); }
        catch (ArgumentException error) { ModelState.AddModelError("",error.Message); return View(model); }
        if (model.ReturnToDispatch) return RedirectToAction("Index", "DispatchBoard");
        TempData["WorkflowNotice"] = "Your default warehouse is saved. Continue with drivers and vehicles.";
        return RedirectToAction("Index", "DeliveryWorkflow");
    }
}

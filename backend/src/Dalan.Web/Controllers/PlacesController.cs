using System.ComponentModel.DataAnnotations;
using Dalan.Application.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Dalan.Web.Controllers;

public sealed class PlaceSearchViewModel
{
    [Required, StringLength(200, MinimumLength = 3)] public string Query { get; set; } = "";
    [BindNever] public IReadOnlyList<PlaceTextResult>? Results { get; set; }
    [BindNever] public string? Error { get; set; }
}

[Authorize(Roles = "Admin,Dispatcher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlacesController(IEnumerable<IGooglePlacesSearch> providers) : Controller
{
    [HttpGet] public IActionResult Index() => View(new PlaceSearchViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(PlaceSearchViewModel model, CancellationToken cancellationToken)
    {
        model.Results = null; model.Error = null;
        if (!ModelState.IsValid) return View(model);
        var provider = providers.SingleOrDefault();
        if (provider is null) { Response.StatusCode = 503; model.Error = "Google Places search has not been configured."; return View(model); }
        try { model.Results = await provider.SearchAsync(model.Query, cancellationToken); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException or InvalidOperationException)
        { Response.StatusCode = 502; model.Error = "Place search is temporarily unavailable. Try again shortly."; }
        return View(model);
    }
}

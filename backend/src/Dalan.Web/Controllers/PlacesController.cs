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
public sealed class PlacesController : Controller
{
    [HttpGet] public IActionResult Index() => RedirectToAction("Index", "Vehicles");

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Index(PlaceSearchViewModel model, CancellationToken cancellationToken)
        => RedirectToAction("Index", "Vehicles");
}

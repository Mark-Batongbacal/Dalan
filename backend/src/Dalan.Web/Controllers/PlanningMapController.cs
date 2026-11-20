using Dalan.Application.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Admin,Dispatcher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlanningMapController(IServiceProvider services) : Controller
{
    public sealed record RouteRequest(IReadOnlyList<GeoPoint?>? Points);
    public sealed record SearchRequest(string? Query);

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16_384)]
    [Authorize(Roles = "Dispatcher")]
    public async Task<IActionResult> Route([FromBody] RouteRequest? request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request?.Points is not { Count: >= 2 and <= 31 } points ||
            points.Any(point => point is null || !point.IsValid))
            return Problem(statusCode: 400, title: "Provide 2–31 valid stop coordinates.");
        var routing = services.GetService<IRoutingService>();
        if (routing is null) return Problem(statusCode: 503, title: "Road routing is unavailable.");
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(30));
            var legs = new List<RoadRoute>();
            for (var i = 1; i < points.Count; i++)
            {
                if (points[i - 1] == points[i]) continue;
                var leg = await routing.GetRouteAsync(points[i - 1]!, points[i]!, budget.Token);
                if (leg is null)
                    return Problem(statusCode: 422, title: "No road route connects these stops. The stop markers are still available.");
                legs.Add(leg);
            }
            return Json(new { legs });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Problem(statusCode: 504, title: "Road routing timed out. Try again."); }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
        { return Problem(statusCode: 502, title: "Road routing is unavailable. The stop markers are still available."); }
    }

    [Authorize(Roles = "Admin,Dispatcher")]
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(4096)]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("staff-address-search")]
    public async Task<IActionResult> Addresses([FromBody] SearchRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Query?.Trim() is not { Length: >= 3 and <= 200 } query)
            return Problem(statusCode: 400, title: "Enter 3–200 characters of a street address.");
        var provider = services.GetService<IGoogleAddressSearch>();
        if (provider is null) return Problem(statusCode: 503, title: "Google address search is not configured. Keep your address and select a map pin manually.");
        try { return Json(new { results = await provider.SearchAsync(query, cancellationToken) }); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException)
        { return Problem(statusCode: 502, title: "Google address search is unavailable. Keep your address and select a map pin manually."); }
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(4096)]
    [Authorize(Roles = "Dispatcher")]
    public async Task<IActionResult> Search([FromBody] SearchRequest? request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request?.Query?.Trim() is not { Length: >= 3 and <= 200 } query)
            return Problem(statusCode: 400, title: "Enter 3–200 characters to search.");
        var search = services.GetService<IPlaceSearchService>();
        if (search is null)
            return Problem(statusCode: 503, title: "Address search is not configured. Select a map point or enter coordinates.");
        try
        {
            return Json(new
            {
                results = await search.SearchAsync(query, cancellationToken),
                attribution = "© OpenStreetMap contributors · Photon"
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Problem(statusCode: 504, title: "Address search timed out. Select a map point or try again."); }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
        { return Problem(statusCode: 502, title: "Address search is unavailable. Select a map point or enter coordinates."); }
    }
}

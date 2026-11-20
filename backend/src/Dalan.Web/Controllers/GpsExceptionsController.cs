using System.Security.Claims;
using Dalan.Application.DriverExecution;
using Dalan.Contracts.DriverExecution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Dalan.Web.Controllers;
[Authorize(Roles = "Admin,Dispatcher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GpsExceptionsController(IGpsExceptionService reviews, IConfiguration configuration) : Controller
{
    [HttpGet("/GpsExceptions")] public IActionResult Index() => View();
    [HttpGet("/api/gps-exceptions")] public async Task<IActionResult> List(CancellationToken token) => Enabled ? Ok(await reviews.List(token)) : StatusCode(503);
    private bool Enabled => bool.TryParse(configuration["DriverGpsProof:Enabled"], out var enabled) && enabled;
    [HttpGet("/api/gps-exceptions/{id:guid}/photo")]
    public async Task<IActionResult> Photo(Guid id, CancellationToken token)
    {
        if (!Enabled) return StatusCode(503);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var photo = await reviews.Photo(id, token);
        return photo is null ? NotFound() : File(photo, "image/jpeg");
    }
    [HttpPost("/api/gps-exceptions/{id:guid}/review"), ValidateAntiForgeryToken, RequestSizeLimit(4096)]
    public async Task<IActionResult> Review(Guid id, [FromBody] GpsExceptionReview request, CancellationToken token)
    {
        if (!Enabled) return StatusCode(503);
        if (!ModelState.IsValid) return BadRequest();
        var result = await reviews.Review(id, request, User.FindFirstValue(ClaimTypes.NameIdentifier)!, token);
        return StatusCode(result.StatusCode);
    }
}

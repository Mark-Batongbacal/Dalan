using System.ComponentModel.DataAnnotations;
using Dalan.Application.Notifications;
using Dalan.Domain.Drivers;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Web.Controllers;

/// <summary>
/// Driver password recovery only. This is separate from staff Identity login.
/// DriverRecovery:Enabled defaults to false until the Codex-owned reset page
/// and the approved email sender are verified.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/driver-recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("driver-recovery")]
public sealed class DriverRecoveryController(
    DalanDbContext db,
    UserManager<IdentityUser> users,
    IEmailSender sender,
    ResendOptions resend,
    IConfiguration configuration,
    ILogger<DriverRecoveryController> logger) : ControllerBase
{
    private static readonly object AcceptedMessage = new
    {
        message = "If an eligible Driver account exists, a password reset link will be emailed."
    };
    private static readonly object InvalidMessage = new
    {
        message = "The reset link is invalid, expired, or has already been used."
    };

    [HttpPost("request")]
    public async Task<IActionResult> RequestReset(
        DriverRecoveryEmailRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetBaseUrl(out var baseUrl) || !resend.Enabled
            || string.IsNullOrWhiteSpace(resend.ApiKey) || string.IsNullOrWhiteSpace(resend.From))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Password recovery is temporarily unavailable." });

        // Publicly indistinguishable for absent, staff, unlinked, inactive and
        // existing Driver addresses. Do not reveal whether an account exists.
        var user = await EligibleDriverAsync(request.Email.Trim(), cancellationToken);
        if (user is null)
            return Accepted(AcceptedMessage);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        // Fragments are not sent to the web server on GET, preventing token
        // disclosure through Caddy query-string access logs. Codex must remove
        // the fragment via history.replaceState before rendering the reset view.
        var url = $"{baseUrl}/DriverRecovery/Reset#email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        var envelope = new EmailEnvelope(
            "driver-recovery-" + Guid.NewGuid().ToString("N"), Guid.Empty,
            resend.From, [user.Email!], "Dalan | Reset your Driver password",
            "A password reset was requested for your Dalan Driver account.\n\n"
            + "Use this private link within 24 hours:\n" + url
            + "\n\nIf you did not request this, ignore the message. Your existing password remains unchanged.");

        try
        {
            await sender.SendAsync(envelope, cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            // Do not log the envelope or token; the response must be identical
            // to the unknown-email case even if email delivery fails.
            logger.LogWarning("Driver recovery provider did not accept the request: {ErrorType}",
                error.GetType().Name);
        }
        return Accepted(AcceptedMessage);
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(
        DriverRecoveryPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetBaseUrl(out _))
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var user = await EligibleDriverAsync(request.Email.Trim(), cancellationToken);
        if (user is null)
            return BadRequest(InvalidMessage);

        var result = await users.ResetPasswordAsync(user, request.Token, request.Password);
        if (!result.Succeeded)
        {
            // Do not leak account existence, the validity of the token, or
            // password-history details in a public response.
            return BadRequest(InvalidMessage);
        }

        // Identity's ResetPasswordAsync rotates the security stamp. The mobile
        // API validates it per request, so existing bearer tokens are revoked.
        return Ok(new { message = "Driver password changed. Sign in again in the mobile app." });
    }

    private async Task<IdentityUser?> EligibleDriverAsync(string email, CancellationToken token)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.EmailConfirmed || !await users.HasPasswordAsync(user)
            || !await users.IsInRoleAsync(user, "Driver"))
            return null;

        var eligible = await (
            from account in db.DriverAccounts.AsNoTracking()
            join driver in db.Drivers.AsNoTracking() on account.DriverId equals driver.Id
            where account.UserId == user.Id && driver.Availability != DriverAvailability.Inactive
            select driver.Id).AnyAsync(token);

        return eligible ? user : null;
    }

    private bool TryGetBaseUrl(out string baseUrl)
    {
        baseUrl = "";
        if (!bool.TryParse(configuration["DriverRecovery:Enabled"], out var enabled) || !enabled)
            return false;
        if (!Uri.TryCreate(configuration["DriverRecovery:PublicBaseUrl"], UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo)
            || url.AbsolutePath != "/" || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            return false;

        baseUrl = url.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}

public sealed class DriverRecoveryEmailRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";
}

public sealed class DriverRecoveryPasswordRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";

    [Required, StringLength(4096)]
    public string Token { get; init; } = "";

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = "";
}

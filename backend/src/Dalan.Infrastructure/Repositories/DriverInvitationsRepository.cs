using System.Data;
using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Dalan.Application.Notifications;
using Dalan.Domain.Drivers;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace Dalan.Infrastructure.Repositories;
public sealed class DriverInvitationsRepository(
    DalanDbContext db,
    UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles,
    IEmailSender sender,
    ResendOptions resend,
    IConfiguration configuration) : LifecycleResponses, IDriverInvitationsRepository
{
    private const string DriverRole = "Driver";
    private const string InvitePurpose = "Dalan.DriverInvitation.v1";

    public async Task<LifecycleResult> Issue(DriverInvitationIssueRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInvitationBaseUrl(out var baseUrl) || !resend.Enabled
            || string.IsNullOrWhiteSpace(resend.ApiKey) || string.IsNullOrWhiteSpace(resend.From))
            return StatusCode(503,
                new { message = "Driver invitations are not configured." });

        var driver = await db.Drivers.SingleOrDefaultAsync(x => x.Id == request.DriverId, cancellationToken);
        if (driver is null) return NotFound();
        if (driver.Availability == DriverAvailability.Inactive)
            return BadRequest(new { message = "An inactive Driver cannot be invited." });

        var email = request.Email.Trim();
        IdentityUser user;

        // Identity and DriverAccount share the same EF connection. Do not leave a linked user
        // with a password set by an administrator if provisioning is interrupted.
        await using (var transaction = await PersistenceTransaction.BeginAsync(db, cancellationToken))
        {
            var account = await db.DriverAccounts.SingleOrDefaultAsync(
                x => x.DriverId == driver.Id, cancellationToken);
            if (account is null)
            {
                if (await users.FindByEmailAsync(email) is not null)
                    return Conflict(new { message = "This email is already registered." });

                if (!await roles.RoleExistsAsync(DriverRole))
                {
                    var createdRole = await roles.CreateAsync(new IdentityRole(DriverRole));
                    if (!createdRole.Succeeded)
                        return StatusCode(500, new { message = "Driver account provisioning failed." });
                }

                user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = false };
                var created = await users.CreateAsync(user); // Deliberately passwordless.
                if (!created.Succeeded)
                    return BadRequest(new { message = "Unable to create the Driver invitation account." });
                var assigned = await users.AddToRoleAsync(user, DriverRole);
                if (!assigned.Succeeded)
                    return StatusCode(500, new { message = "Driver account provisioning failed." });

                db.DriverAccounts.Add(new DriverAccount { DriverId = driver.Id, UserId = user.Id });
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                user = await users.FindByIdAsync(account.UserId)
                    ?? throw new InvalidOperationException("A Driver account link has no Identity user.");

                if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)
                    || await users.HasPasswordAsync(user) || user.EmailConfirmed
                    || !await users.IsInRoleAsync(user, DriverRole))
                    return Conflict(new { message = "This Driver already has a linked or incompatible account." });

                // A reissued invitation invalidates all older invitation tokens.
                var rotated = await users.UpdateSecurityStampAsync(user);
                if (!rotated.Succeeded)
                    return StatusCode(500, new { message = "Unable to reissue this invitation." });
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // The user token is scoped to the Identity security stamp, this purpose, and
        // DataProtectionTokenProviderOptions.TokenLifespan (24 hours by default).
        // No raw token is persisted in Dalan's database.
        var token = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, InvitePurpose);

        // The secret is in the URL fragment, not an HTTP query string: browsers do not send
        // the fragment to Caddy/ASP.NET. The activation page parses it client-side and
        // submits it only to the activation POST endpoint (no logging/analytics of the fragment).
        var link = $"{baseUrl}/DriverInvitations/Activate#email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        var message = new EmailEnvelope(
            "driver-invite-" + Guid.NewGuid().ToString("N"), Guid.Empty, resend.From,
            [email], "Dalan | Set up your Driver account",
            "You were invited to access the Dalan Driver app.\n\n"
            + "Open this private link to set your password (valid for up to 24 hours):\n"
            + link + "\n\nIf you did not expect this invitation, ignore this message.");

        try
        {
            // The configured sender routes credentials through the protected outbox. An ambiguous failure can
            // be retried manually through Issue, which rotates the previous token.
            await sender.SendAsync(message, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException
            or InvalidOperationException or System.Text.Json.JsonException or InvalidDataException)
        {
            return StatusCode(502,
                new { message = "The Driver account is pending, but invitation delivery failed. Retry the invitation." });
        }

        return Accepted(new { message = "Invitation submitted for delivery. Check email delivery status; inbox delivery is not guaranteed." });
    }

    public async Task<LifecycleResult> Activate(DriverInvitationActivationRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInvitationBaseUrl(out _))
            return StatusCode(503);

        var generic = new { message = "Invalid, expired, or previously used invitation." };
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.EmailConfirmed || await users.HasPasswordAsync(user)
            || !await users.IsInRoleAsync(user, DriverRole))
            return BadRequest(generic);

        // The account must still be linked to a non-inactive Driver.
        var eligible = await (from account in db.DriverAccounts
                              join driver in db.Drivers on account.DriverId equals driver.Id
                              where account.UserId == user.Id && driver.Availability != DriverAvailability.Inactive
                              select account.DriverId).AnyAsync(cancellationToken);
        if (!eligible || !await users.VerifyUserTokenAsync(
                user, TokenOptions.DefaultProvider, InvitePurpose, request.Token))
            return BadRequest(generic);

        // Keep password and email-confirmation activation atomic, including failed Identity writes.
        await using var transaction = await PersistenceTransaction.BeginAsync(db, cancellationToken);
        user = await users.FindByIdAsync(user.Id);
        if (user is null || user.EmailConfirmed || await users.HasPasswordAsync(user)
            || !await users.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, InvitePurpose, request.Token))
            return BadRequest(generic);

        var added = await users.AddPasswordAsync(user, request.Password);
        if (!added.Succeeded)
            return BadRequest(new
            {
                message = "Password does not meet the account requirements.",
                errors = added.Errors.Select(e => e.Description).ToArray()
            });

        user.EmailConfirmed = true;
        var confirmed = await users.UpdateAsync(user);
        if (!confirmed.Succeeded)
            return StatusCode(500, new { message = "Account activation failed. Try again." });

        await transaction.CommitAsync(cancellationToken);
        return Ok(new { message = "Your Driver password is set. Sign in using the mobile app." });
    }

    private bool TryGetInvitationBaseUrl(out string baseUrl)
    {
        baseUrl = "";
        if (!bool.TryParse(configuration["DriverInvitations:Enabled"], out var enabled) || !enabled)
            return false;
        var configured = configuration["DriverInvitations:PublicBaseUrl"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        baseUrl = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}

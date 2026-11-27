using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Web;

public static class PrototypeManagerClaimTypes
{
    public const string Team = "team";
    public const string Directorate = "directorate";
    public const string BusinessAreaId = "business_area_id";
}

public static class ClaimsPrincipalExtensions
{
    public static string GetEmail(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue("preferred_username")
        ?? principal.FindFirstValue("emails")
        ?? principal.FindFirstValue(ClaimTypes.Upn)
        ?? principal.FindFirstValue("upn")
        ?? principal.Identity?.Name
        ?? string.Empty;

    public static string GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name)
        ?? principal.FindFirstValue("name")
        ?? principal.GetEmail();

    public static string? GetEntraObjectId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("oid")
        ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

    public static string GetTeam(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(PrototypeManagerClaimTypes.Team) ?? string.Empty;

    public static string GetDirectorate(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(PrototypeManagerClaimTypes.Directorate) ?? string.Empty;

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.IsInRole(Domain.AppRoles.Admin);

    public static bool IsSupport(this ClaimsPrincipal principal) => principal.IsInRole(Domain.AppRoles.Support);

    public static IReadOnlyList<Guid> GetAssignedBusinessAreaIds(this ClaimsPrincipal principal) =>
        principal.FindAll(PrototypeManagerClaimTypes.BusinessAreaId)
            .Select(claim => Guid.TryParse(claim.Value, out var value) ? value : Guid.Empty)
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToList();
}

internal static class EntraClaims
{
    public static void EnsureEmailClaim(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(principal.FindFirstValue(ClaimTypes.Email)))
        {
            return;
        }

        var email =
            principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue("emails")
            ?? principal.FindFirstValue(ClaimTypes.Upn)
            ?? principal.FindFirstValue("upn");

        if (!string.IsNullOrWhiteSpace(email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
        }
    }
}

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<DevelopmentUserOptions> developmentUserOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";

    private readonly DevelopmentUserOptions _developmentUser = developmentUserOptions.Value;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_developmentUser.Enabled || string.IsNullOrWhiteSpace(_developmentUser.Email))
        {
            return Task.FromResult(AuthenticateResult.Fail("Development user is not configured."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, _developmentUser.DisplayName),
            new(ClaimTypes.Email, _developmentUser.Email),
            new(PrototypeManagerClaimTypes.Team, _developmentUser.Team),
            new(PrototypeManagerClaimTypes.Directorate, _developmentUser.Directorate)
        };

        claims.AddRange(_developmentUser.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class AppUserClaimsTransformation(
    PrototypeManagerDbContext dbContext) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        var normalizedEmail = principal.GetEmail().Trim().ToLowerInvariant();
        var entraObjectId = principal.GetEntraObjectId();
        var displayName = principal.GetDisplayName().Trim();

        if (string.IsNullOrWhiteSpace(normalizedEmail) && string.IsNullOrWhiteSpace(entraObjectId))
        {
            return principal;
        }

        var identity = principal.Identities.FirstOrDefault(item => item.IsAuthenticated);
        if (identity is null)
        {
            return principal;
        }

        // Prefer matching a pre-provisioned access record by email; fall back to Entra object id.
        AppUser? appUser = null;
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            appUser = await dbContext.AppUsers
                .Include(x => x.BusinessAreaAssignments)
                .SingleOrDefaultAsync(x => x.Email == normalizedEmail);
        }

        if (appUser is null && !string.IsNullOrWhiteSpace(entraObjectId))
        {
            appUser = await dbContext.AppUsers
                .Include(x => x.BusinessAreaAssignments)
                .SingleOrDefaultAsync(x => x.EntraObjectId == entraObjectId);
        }

        if (appUser is null)
        {
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return principal;
            }

            appUser = new AppUser
            {
                Email = normalizedEmail,
                DisplayName = displayName,
                EntraObjectId = string.IsNullOrWhiteSpace(entraObjectId) ? null : entraObjectId,
                IsAdmin = principal.IsInRole(AppRoles.Admin),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                LastSignedInAt = DateTimeOffset.UtcNow
            };

            dbContext.AppUsers.Add(appUser);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            var changed = false;

            if (!string.IsNullOrWhiteSpace(normalizedEmail) &&
                !string.Equals(appUser.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                appUser.Email = normalizedEmail;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(displayName) &&
                !string.Equals(appUser.DisplayName, displayName, StringComparison.Ordinal))
            {
                appUser.DisplayName = displayName;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(entraObjectId) &&
                !string.Equals(appUser.EntraObjectId, entraObjectId, StringComparison.Ordinal))
            {
                appUser.EntraObjectId = entraObjectId;
                changed = true;
            }

            var shouldTouchSignIn =
                appUser.LastSignedInAt is null ||
                appUser.LastSignedInAt < DateTimeOffset.UtcNow.AddHours(-1);

            if (shouldTouchSignIn)
            {
                appUser.LastSignedInAt = DateTimeOffset.UtcNow;
                changed = true;
            }

            if (changed)
            {
                appUser.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync();
            }
        }

        if (appUser.IsAdmin && !principal.IsInRole(AppRoles.Admin))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, AppRoles.Admin));
        }

        foreach (var businessAreaId in appUser.BusinessAreaAssignments.Select(assignment => assignment.BusinessAreaId))
        {
            if (!identity.HasClaim(PrototypeManagerClaimTypes.BusinessAreaId, businessAreaId.ToString()))
            {
                identity.AddClaim(new Claim(PrototypeManagerClaimTypes.BusinessAreaId, businessAreaId.ToString()));
            }
        }

        return principal;
    }
}

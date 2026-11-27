using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    internal const string Transformed = "pm_claims_transformed";
}

public static class ClaimsPrincipalExtensions
{
    public static string GetEmail(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue("preferred_username")
        ?? principal.FindFirstValue("email")
        ?? principal.FindFirstValue("emails")
        ?? principal.FindFirstValue(ClaimTypes.Upn)
        ?? principal.FindFirstValue("upn")
        ?? string.Empty;

    public static string GetDisplayName(this ClaimsPrincipal principal)
    {
        var name =
            FirstNonEmpty(
                principal.FindFirstValue("name"),
                principal.FindFirstValue(ClaimTypes.Name),
                CombineName(
                    principal.FindFirstValue("given_name") ?? principal.FindFirstValue(ClaimTypes.GivenName),
                    principal.FindFirstValue("family_name") ?? principal.FindFirstValue(ClaimTypes.Surname)))
            ?? principal.GetEmail();

        return name;
    }

    public static string? GetEntraObjectId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("oid")
        ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? CombineName(string? givenName, string? familyName)
    {
        var parts = new[] { givenName, familyName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim());
        var combined = string.Join(' ', parts);
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

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
    public static void EnsureProfileClaims(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(principal.FindFirstValue(ClaimTypes.Email)))
        {
            var email =
                principal.FindFirstValue("preferred_username")
                ?? principal.FindFirstValue("email")
                ?? principal.FindFirstValue("emails")
                ?? principal.FindFirstValue(ClaimTypes.Upn)
                ?? principal.FindFirstValue("upn");

            if (!string.IsNullOrWhiteSpace(email))
            {
                identity.AddClaim(new Claim(ClaimTypes.Email, email.Trim()));
            }
        }

        var displayName = principal.GetDisplayName();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        // Keep ClaimTypes.Name aligned with the Entra profile so Identity.Name / UI helpers agree.
        var existingName = identity.FindFirst(ClaimTypes.Name);
        if (existingName is null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
        }
        else if (!string.Equals(existingName.Value, displayName, StringComparison.Ordinal))
        {
            identity.RemoveClaim(existingName);
            identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
        }

        if (string.IsNullOrWhiteSpace(principal.FindFirstValue("name")))
        {
            identity.AddClaim(new Claim("name", displayName));
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
    PrototypeManagerDbContext dbContext,
    IMemoryCache memoryCache,
    IHttpContextAccessor httpContextAccessor) : IClaimsTransformation
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string HttpContextItemKey = "pm.claims.transformed";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        if (principal.HasClaim(PrototypeManagerClaimTypes.Transformed, "1"))
        {
            return principal;
        }

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Items[HttpContextItemKey] is true)
        {
            return principal;
        }

        if (httpContext is not null)
        {
            httpContext.Items[HttpContextItemKey] = true;
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

        var cacheKey = BuildCacheKey(normalizedEmail, entraObjectId);
        if (memoryCache.TryGetValue(cacheKey, out CachedAppUserClaims? cached) && cached is not null)
        {
            ApplyClaims(identity, principal, cached);
            return principal;
        }

        var snapshot = await LoadOrCreateAppUserAsync(normalizedEmail, entraObjectId, displayName, principal);
        if (snapshot is null)
        {
            return principal;
        }

        memoryCache.Set(cacheKey, snapshot, CacheDuration);
        ApplyClaims(identity, principal, snapshot);
        return principal;
    }

    private async Task<CachedAppUserClaims?> LoadOrCreateAppUserAsync(
        string normalizedEmail,
        string? entraObjectId,
        string displayName,
        ClaimsPrincipal principal)
    {
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
                return null;
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

        var resolvedDisplayName = !string.IsNullOrWhiteSpace(displayName) &&
                                  !string.Equals(displayName, normalizedEmail, StringComparison.OrdinalIgnoreCase)
            ? displayName
            : FirstNonEmpty(appUser.DisplayName, displayName, normalizedEmail) ?? normalizedEmail;

        return new CachedAppUserClaims(
            resolvedDisplayName,
            appUser.IsAdmin,
            appUser.BusinessAreaAssignments.Select(assignment => assignment.BusinessAreaId).ToArray());
    }

    private static void ApplyClaims(
        ClaimsIdentity identity,
        ClaimsPrincipal principal,
        CachedAppUserClaims snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.DisplayName))
        {
            ReplaceClaim(identity, ClaimTypes.Name, snapshot.DisplayName);
            if (string.IsNullOrWhiteSpace(principal.FindFirstValue("name")))
            {
                identity.AddClaim(new Claim("name", snapshot.DisplayName));
            }
        }

        if (snapshot.IsAdmin && !principal.IsInRole(AppRoles.Admin))
        {
            // Use this identity's RoleClaimType (Entra sets it to "roles", not ClaimTypes.Role).
            identity.AddClaim(new Claim(identity.RoleClaimType, AppRoles.Admin));
        }

        foreach (var businessAreaId in snapshot.BusinessAreaIds)
        {
            if (!identity.HasClaim(PrototypeManagerClaimTypes.BusinessAreaId, businessAreaId.ToString()))
            {
                identity.AddClaim(new Claim(PrototypeManagerClaimTypes.BusinessAreaId, businessAreaId.ToString()));
            }
        }

        if (!identity.HasClaim(PrototypeManagerClaimTypes.Transformed, "1"))
        {
            identity.AddClaim(new Claim(PrototypeManagerClaimTypes.Transformed, "1"));
        }
    }

    private static string BuildCacheKey(string normalizedEmail, string? entraObjectId) =>
        !string.IsNullOrWhiteSpace(normalizedEmail)
            ? $"appuser:claims:email:{normalizedEmail}"
            : $"appuser:claims:oid:{entraObjectId}";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static void ReplaceClaim(ClaimsIdentity identity, string type, string value)
    {
        var existing = identity.FindFirst(type);
        if (existing is not null)
        {
            identity.RemoveClaim(existing);
        }

        identity.AddClaim(new Claim(type, value));
    }

    private sealed record CachedAppUserClaims(
        string DisplayName,
        bool IsAdmin,
        IReadOnlyList<Guid> BusinessAreaIds);
}

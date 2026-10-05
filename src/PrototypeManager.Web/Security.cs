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
        ?? principal.FindFirstValue(ClaimTypes.Upn)
        ?? principal.Identity?.Name
        ?? string.Empty;

    public static string GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? principal.GetEmail();

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

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return principal;
        }

        var identity = principal.Identities.FirstOrDefault(identity => identity.IsAuthenticated);

        if (identity is null)
        {
            return principal;
        }

        var appUser = await dbContext.AppUsers
            .Include(x => x.BusinessAreaAssignments)
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail);

        if (appUser is null)
        {
            appUser = new AppUser
            {
                Email = normalizedEmail,
                DisplayName = principal.GetDisplayName(),
                IsAdmin = principal.IsInRole(AppRoles.Admin),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            dbContext.AppUsers.Add(appUser);
            await dbContext.SaveChangesAsync();
        }
        else if (string.IsNullOrWhiteSpace(appUser.DisplayName) && !string.IsNullOrWhiteSpace(principal.GetDisplayName()))
        {
            appUser.DisplayName = principal.GetDisplayName();
            appUser.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
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

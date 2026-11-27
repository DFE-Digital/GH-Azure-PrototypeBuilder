using Dfe.Frontend.AspNetCore;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;
using PrototypeManager.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGovUkFrontend();
builder.Services.AddHttpContextAccessor();
builder.Services.AddPrototypeManagerInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddTransient<IClaimsTransformation, AppUserClaimsTransformation>();

var developmentUserSection = builder.Configuration.GetSection("DevelopmentUser");
var useDevelopmentAuth =
    builder.Environment.IsDevelopment() &&
    developmentUserSection.GetValue("Enabled", false) &&
    !string.IsNullOrWhiteSpace(developmentUserSection["Email"]);

if (useDevelopmentAuth)
{
    // Opt-in local bypass: fake signed-in user from DevelopmentUser config (SQLite).
    builder.Services
        .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName,
            _ => { });
}
else
{
    // Microsoft Entra ID work/school accounts (local or hosted).
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            options.DefaultSignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

    builder.Services.Configure<OpenIdConnectOptions>(
        OpenIdConnectDefaults.AuthenticationScheme,
        options =>
        {
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";
            options.MapInboundClaims = false;
            if (!options.Scope.Contains("profile"))
            {
                options.Scope.Add("profile");
            }

            if (!options.Scope.Contains("email"))
            {
                options.Scope.Add("email");
            }

            options.Events ??= new OpenIdConnectEvents();
            var previous = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                if (previous is not null)
                {
                    await previous(context);
                }

                EntraClaims.EnsureProfileClaims(context.Principal);
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    // Every endpoint requires a signed-in user unless marked AllowAnonymous.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.DefaultPolicy = options.FallbackPolicy;

    options.AddPolicy("AdminOnly", policy => policy.RequireRole(AppRoles.Admin));
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PrototypeManagerDbContext>("database")
    .AddCheck<ProvisioningConfigurationHealthCheck>("provisioning_mode");

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/SignedOut");
    options.Conventions.AllowAnonymousToPage("/Error");

    options.Conventions.AddPageRoute("/Prototypes/List", "prototypes");
    options.Conventions.AddPageRoute("/Prototypes/Details", "prototypes/{id:guid}");
    options.Conventions.AddPageRoute("/Prototypes/Open", "prototypes/{id:guid}/open");
    options.Conventions.AddPageRoute("/Prototypes/Extend", "prototypes/{id:guid}/extend");
    options.Conventions.AddPageRoute("/Prototypes/Password", "prototypes/{id:guid}/password");
    options.Conventions.AddPageRoute("/Prototypes/BusinessArea", "prototypes/{id:guid}/business-area");
    options.Conventions.AddPageRoute("/Prototypes/Owner", "prototypes/{id:guid}/owner");
    options.Conventions.AddPageRoute("/Prototypes/ConfigureGithub", "prototypes/{id:guid}/configure-github");
    options.Conventions.AddPageRoute("/Prototypes/Archive", "prototypes/{id:guid}/archive");
    options.Conventions.AddPageRoute("/Prototypes/Delete", "prototypes/{id:guid}/delete");
    options.Conventions.AddPageRoute("/Prototypes/Retry", "prototypes/{id:guid}/retry");
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
})
.AddApplicationPart(DfeFrontendAspNetCore.Assembly);

var app = builder.Build();

await app.Services.InitialiseDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseGovUkFrontend();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapStaticAssets();
app.MapRazorPages()
    .RequireAuthorization()
    .WithStaticAssets();

app.Run();

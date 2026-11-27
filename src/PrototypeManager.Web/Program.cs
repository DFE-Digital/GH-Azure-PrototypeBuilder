using Dfe.Frontend.AspNetCore;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;
using PrototypeManager.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGovUkFrontend();
builder.Services.AddPrototypeManagerInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddTransient<IClaimsTransformation, AppUserClaimsTransformation>();

if (builder.Environment.IsDevelopment())
{
    // Local only: fake Windows/DfE user from DevelopmentUser config (SQLite).
    builder.Services
        .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName,
            _ => { });
}
else
{
    // Hosted: Microsoft Entra ID (work/school accounts).
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

    builder.Services.Configure<OpenIdConnectOptions>(
        OpenIdConnectDefaults.AuthenticationScheme,
        options =>
        {
            // Prefer organisational accounts; map common Entra email claims for app user lookup.
            options.TokenValidationParameters.NameClaimType = "name";
            options.Events ??= new OpenIdConnectEvents();
            var previous = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                if (previous is not null)
                {
                    await previous(context);
                }

                EntraClaims.EnsureEmailClaim(context.Principal);
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("AdminOnly", policy => policy.RequireRole(AppRoles.Admin));
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PrototypeManagerDbContext>("database")
    .AddCheck<ProvisioningConfigurationHealthCheck>("provisioning_mode");

builder.Services.AddRazorPages(options =>
{
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
    options.Conventions.AllowAnonymousToPage("/Account/SignedOut");
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
app.MapRazorPages().WithStaticAssets();

app.Run();

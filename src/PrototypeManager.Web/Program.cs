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

GitHubConfiguration.ApplyAppsettingsToken(builder);

builder.Services.AddGovUkFrontend();
builder.Services.AddPrototypeManagerInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddTransient<IClaimsTransformation, AppUserClaimsTransformation>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = builder.Environment.IsDevelopment()
        ? DevelopmentAuthenticationHandler.SchemeName
        : OpenIdConnectDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = builder.Environment.IsDevelopment()
        ? DevelopmentAuthenticationHandler.SchemeName
        : OpenIdConnectDefaults.AuthenticationScheme;
})
.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
    DevelopmentAuthenticationHandler.SchemeName,
    _ => { });

if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
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

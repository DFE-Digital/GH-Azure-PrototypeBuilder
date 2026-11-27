# Prototype Manager

Prototype Manager is an internal ASP.NET Core app for creating and managing tightly controlled Azure hosting environments for GOV.UK Prototype Kit and DfE Frontend prototypes.

## What the app does

- creates prototype records and queues provisioning jobs
- provisions mock or Azure-backed hosting environments behind an interface
- applies naming conventions, tags, and free-tier cost controls
- shows prototype status, live URL, audit history, and deployment history
- creates or links GitHub repositories and can configure deployment via a GitHub App

## How to run locally

1. Make sure .NET 10 SDK is installed.
2. Copy the example config files and fill in local values:

   ```bash
   cp src/PrototypeManager.Web/appsettings.example.json src/PrototypeManager.Web/appsettings.json
   cp src/PrototypeManager.Web/appsettings.Development.example.json src/PrototypeManager.Web/appsettings.Development.json
   ```

3. From the repository root, run:

   ```bash
   dotnet build PrototypeManager.slnx
   dotnet run --project src/PrototypeManager.Web
   ```

4. Open the local URL shown by ASP.NET Core.

In Development, auth uses the configured development user from `src/PrototypeManager.Web/appsettings.Development.json`, and the local database is SQLite (`Data Source=prototype-manager.db`) so local work does not touch shared Dev data.
Hosted Dev uses Azure SQL via the App Service connection string. The provider is chosen from the connection string (SQLite vs SQL Server).

## How to run tests

From the repository root:

```bash
dotnet test PrototypeManager.slnx
```

Or target the test project only:

```bash
dotnet test tests/PrototypeManager.Tests/PrototypeManager.Tests.csproj
```

Useful options:

```bash
# filter by name
dotnet test PrototypeManager.slnx --filter "FullyQualifiedName~GitHubPrototypeCodebaseAssessor"

# quieter console
dotnet test PrototypeManager.slnx --logger "console;verbosity=minimal"
```

Tests do not need Azure or live GitHub credentials. Integrations that call external APIs are covered with null/fakes or early-return paths in unit tests.

### Local configuration (do not commit secrets)

Only the anonymised example files are committed:

- `src/PrototypeManager.Web/appsettings.example.json`
- `src/PrototypeManager.Web/appsettings.Development.example.json`

These are gitignored and must stay local (or use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)):

- `src/PrototypeManager.Web/appsettings.json`
- `src/PrototypeManager.Web/appsettings.Development.json`
- `*.pem` private keys

Never put real subscription IDs, API tokens, Notify keys, GitHub App private keys, or installation IDs into the example files or the README.

## Deploy Development

Pushes to the `dev` branch run [`.github/workflows/deploy-development.yml`](.github/workflows/deploy-development.yml), which tests, publishes, and deploys Prototype Manager to the Development App Service:

https://s186d01-app-prototype-manager-h7bff3fsesaqdkg0.westeurope-01.azurewebsites.net

Azure resource name used by the workflow: `s186d01-app-prototype-manager` (must match `msdeploySite` in the publish profile).

### One-time GitHub setup

1. In the Azure portal, open the Dev web app → **Get publish profile** and download the `.PublishSettings` file.
2. In GitHub: **Settings → Environments → Development** (create it if needed).
3. Add an environment secret named `AZURE_WEBAPP_PUBLISH_PROFILE_DEV` whose value is the full contents of that publish profile file.
4. Push to `dev` or run **Actions → Deploy Development → Run workflow**.

### Azure app settings (Dev)

Do not put live connection strings or API keys in git. Configure them on the App Service, for example:

- `ConnectionStrings__PrototypeManager` — Azure SQL connection string for `prototype-manager-dev`
- `ASPNETCORE_ENVIRONMENT` — `Development` or `Production` as appropriate for hosted Dev
- Plus GitHub, Compass, Notify, Azure AD, and provisioning settings as needed

When Test and Production App Services exist, use the same pattern with distinct secrets (`AZURE_WEBAPP_PUBLISH_PROFILE_TEST`, `AZURE_WEBAPP_PUBLISH_PROFILE_PROD`) on the `Test` / `Production` GitHub environments and matching deploy workflows for the `test` / `production` branches.

## Mock provisioning

- `Provisioning:Mode` defaults to `Mock` in examples until you opt into Azure
- the worker creates Azure-safe names and a fake Azure Websites URL
- passwords are kept in an in-memory job secret store for the MVP
- no real Azure resources are created in mock mode

## Azure provisioning

Set `Provisioning:Mode` to `Azure` and configure:

- `Provisioning:AzureSubscriptionId`
- `Provisioning:Location`
- `Provisioning:CompiledTemplatePath`
- Azure credentials via managed identity or local developer credentials supported by `DefaultAzureCredential`

The runtime app deploys a compiled ARM template using the Azure Resource Manager SDK.

## Required configuration

- `ConnectionStrings:PrototypeManager`
- `Provisioning:*`
- `CostControl:*`
- `AzureAd:*` for hosted environments
- `DevelopmentUser:*` for local development only
- `GitHub:AppId`, `GitHub:InstallationId`, and `GitHub:PrivateKey` (or `PrivateKeyPath`) for GitHub App automation
- `CompassApi:BaseUrl` and `CompassApi:ApiToken` to link prototypes to COMPASS work items and products
- `Notify:ApiKey` and `Notify:PrototypeReadyTemplateId` for GOV.UK Notify emails when a prototype becomes ready

## GitHub App authentication

Prototype Manager uses a GitHub App (installed on the organisation) to create repositories, commit workflows, and manage Actions secrets.

In gitignored `appsettings.json` / `appsettings.Development.json`, or user secrets:

```json
"GitHub": {
  "AppId": 0,
  "InstallationId": 0,
  "PrivateKey": "",
  "PrivateKeyPath": "../../docs/<your-github-app>.private-key.pem"
}
```

Prefer `PrivateKeyPath` pointing at a local `.pem` file (gitignored via `*.pem`) instead of embedding the key in JSON.

`PersonalAccessToken` remains supported only as a fallback.

## GOV.UK Notify

When Azure hosting finishes creating a prototype, Prototype Manager emails the owner via [GOV.UK Notify](https://docs.notifications.service.gov.uk/net.html) using a template with `((subject))` and `((body))` personalisation.

Configure in gitignored `appsettings.json` / `appsettings.Development.json`:

```json
"Notify": {
  "ApiKey": "<govuk-notify-api-key>",
  "PrototypeReadyTemplateId": "<notify-template-id>",
  "HelpEmail": "design.ops@education.gov.uk"
}
```

The email includes the prototype URL, password, GitHub clone URL (when available), how to add team contacts, and DesignOps help details. Email failures are logged and do not fail provisioning.

## GOV.UK frontend setup

The web app uses `GovUk.Frontend.AspNetCore` and the GOV.UK page template. Static GOV.UK assets are restored as part of the web project build because `EnableGovUkFrontendSupport` is enabled in `src/PrototypeManager.Web/PrototypeManager.Web.csproj`.

## Compile the Bicep template

Compile the template before using Azure mode:

```bash
mkdir -p infra/compiled
az bicep build --file infra/prototype-hosting/main.bicep --outfile infra/compiled/prototype-hosting.json
```

## Security notes

- all pages require authentication except health checks
- Development auth is only enabled in Development
- POST actions use Razor Pages anti-forgery protection
- prototype passwords are never stored in the database as plain text
- users cannot choose arbitrary Azure names, regions, SKUs, or app settings
- secrets live only in gitignored local config, user secrets, or the host environment — not in source control

## Cost control notes

- region defaults to `westeurope`
- SKU defaults to `F1`
- Free (F1) App Service plans are shared in a pool (`s186d01-pt-rg-plans` / `s186d01-pt-asp-free-01`…`10`), up to 10 apps per plan and 10 plans per region
- temporary B1/B2 escalations move the web app to a dedicated plan in the prototype resource group, then back to the Free pool
- deleting a prototype removes its resource group only; shared Free pool plans are left for reuse
- required tags are applied by the Azure provisioner
- expiry dates are capped by configuration

## Known MVP limitations

- Azure provisioning expects a compiled ARM template to exist already
- passwords are passed during provisioning, but Key Vault is not implemented yet
- the exception request workflow is modelled but still basic
- expiry reminder emails are not implemented (ready emails via Notify are)
- custom domains, databases, and paid plans are out of scope for the MVP

## Next steps

- add richer owner and deputy owner update flows
- compile the Bicep template automatically in CI
- move provisioning secrets to Key Vault

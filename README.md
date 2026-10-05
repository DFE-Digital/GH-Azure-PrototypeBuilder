# Prototype Manager

Prototype Manager is an internal ASP.NET Core app for creating and managing tightly controlled Azure hosting environments for GOV.UK Prototype Kit and DfE Frontend prototypes.

## What the app does

- creates prototype records and queues provisioning jobs
- provisions mock or Azure-backed hosting environments behind an interface
- applies naming conventions, tags, and free-tier cost controls
- shows prototype status, live URL, audit history, and deployment history
- generates a GitHub Actions workflow users can copy into their repository

## How to run locally

1. Make sure .NET 10 SDK is installed.
2. Copy the example config files and fill in local values (these files are gitignored):

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

In Development, the app uses SQLite and a configured development user from `src/PrototypeManager.Web/appsettings.Development.json`.

## Mock provisioning

- `Provisioning:Mode` defaults to `Mock`
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

## Cost control notes

- region defaults to `westeurope`
- SKU defaults to `F1`
- required tags are applied by the Azure provisioner
- expiry dates are capped by configuration

## Known MVP limitations

- the generated GitHub workflow is shown in the UI but not committed automatically
- Azure provisioning expects a compiled ARM template to exist already
- passwords are passed during provisioning, but Key Vault is not implemented yet
- the exception request workflow is modelled but still basic
- email reminders are not implemented
- custom domains, databases, and paid plans are out of scope for the MVP

## Next steps

- add richer owner and deputy owner update flows
- compile the Bicep template automatically in CI
- move provisioning secrets to Key Vault
- replace the copy-paste GitHub workflow with a GitHub App flow

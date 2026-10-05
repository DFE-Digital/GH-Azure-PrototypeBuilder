# Prototype Hosting Bicep

This directory contains the locked-down Azure infrastructure template for Prototype Manager.

## What it creates

- Linux App Service plan
- Node.js Web App
- Required app settings
- HTTPS-only hosting
- FTPS disabled
- Required tags

## Compile to ARM JSON

The ASP.NET Core app deploys a compiled ARM template. It does not call the Azure CLI at runtime.

Compile the Bicep template before running Azure provisioning:

```bash
mkdir -p infra/compiled
az bicep build --file infra/prototype-hosting/main.bicep --outfile infra/compiled/prototype-hosting.json
```

If you prefer the standalone Bicep CLI:

```bash
bicep build infra/prototype-hosting/main.bicep --outfile infra/compiled/prototype-hosting.json
```

## Parameters

- `location`
- `appServicePlanName`
- `webAppName`
- `nodeRuntime`
- `prototypePassword`
- `tags`

## Cost controls

- App Service plan is fixed to `F1`
- runtime defaults to `NODE|24-lts`
- resource tags are required for every deployment

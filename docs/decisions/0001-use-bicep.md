# 0001 Use Bicep

## Status

Accepted

## Context

Prototype Manager provisions a tightly controlled set of Azure resources. The infrastructure definition needs to be readable, versioned with the application, and easy to compile into an ARM deployment artifact that the web app can submit without shelling out to Azure CLI at runtime.

## Decision

Use Bicep for Azure infrastructure templates.

Compile Bicep to ARM JSON during local setup or CI, and have the ASP.NET Core application deploy the compiled template using the Azure Resource Manager SDK.

## Consequences

- Infrastructure stays close to native Azure resource schemas.
- The application can deploy from a static JSON artifact at runtime.
- Template changes stay reviewable alongside application code.
- Terraform is not used in this repository.

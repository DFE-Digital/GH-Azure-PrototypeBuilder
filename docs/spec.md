Build an internal ASP.NET Core web app called **Prototype Manager**.

The app allows DfE users to create and manage hosted GOV.UK Prototype Kit and DfE Frontend prototypes in Azure without giving users direct access to Azure.

Use **Bicep** for Azure infrastructure. Do not use Terraform.

## Product goal

Create a secure internal web app that lets a user:

* create a new prototype hosting environment
* connect it to a GitHub repository
* provision the approved Azure resources
* apply cost and safety controls
* see the live prototype URL
* manage owner, deputy owner and expiry date
* change the prototype password
* archive or delete the prototype

The app must prevent accidental Azure costs by only allowing approved resource types, approved regions, approved SKUs and required tags.

## Technical stack

Use:

* ASP.NET Core 10
* C#
* Razor Pages or MVC this 
* Entity Framework Core
* SQLite for local development
* SQL Server-compatible provider via connection string for hosted environments
* Azure.Identity
* Azure.ResourceManager
* Azure.ResourceManager.Resources
* Azure.ResourceManager.AppService
* Microsoft.Identity.Web for Microsoft Entra ID authentication
* Bicep for infrastructure templates
* background worker service for provisioning jobs
* dependency injection throughout
* xUnit for tests

Create the solution using this structure:

```text
PrototypeManager/
  src/
    PrototypeManager.Web/
    PrototypeManager.Application/
    PrototypeManager.Infrastructure/
    PrototypeManager.Domain/
  tests/
    PrototypeManager.Tests/
  infra/
    prototype-hosting/
      main.bicep
      README.md
  docs/
    decisions/
      0001-use-bicep.md
      0002-avoid-direct-azure-access.md
```

## Core principles

The app must follow these principles:

1. Users do not need Azure portal access.
2. Users cannot choose paid Azure SKUs.
3. Users cannot create arbitrary Azure resources.
4. Every prototype must have an owner, deputy owner and expiry date.
5. Every Azure resource must be tagged.
6. Every prototype must be password protected.
7. The app must keep an audit trail.
8. The app must support a mock provisioning mode for local development.
9. Real Azure provisioning must be behind an interface.
10. The app must be deployable without changing core application code.

## Roles

Implement these roles:

```text
User
- can create prototypes
- can manage prototypes they own
- can view team prototypes

Admin
- can view all prototypes
- can approve exceptions
- can delete any prototype
- can retry failed provisioning jobs
- can see expired prototypes

Support
- can view all prototypes
- can retry failed provisioning jobs
- can update metadata
- cannot approve paid exceptions
```

For local development, add a simple development user provider that can be configured in `appsettings.Development.json`.

Do not make the development user provider active outside Development environment.

## MVP user journeys

### Create a prototype

A signed-in user can create a prototype by entering:

* prototype name
* service or project name
* team
* directorate
* GitHub repository URL
* owner email
* deputy owner email
* expiry date
* reason for prototype
* confirmation that it contains no real user data
* confirmation that it is not a production service
* prototype password

When the form is submitted:

1. validate the input
2. create a prototype record with status `Requested`
3. create a provisioning job with status `Queued`
4. show a status page

The background worker should then:

1. change prototype status to `Provisioning`
2. create the Azure resource group
3. deploy the Bicep template
4. store the generated Azure resource names
5. set the prototype URL
6. change prototype status to `Live`
7. create an audit event

If provisioning fails:

1. set prototype status to `Failed`
2. store the error message
3. create an audit event
4. allow Admin or Support to retry

### View prototypes

Create a dashboard showing:

* my prototypes
* team prototypes
* prototypes expiring soon
* failed deployments

Each row should show:

* name
* team
* owner
* status
* live URL
* expiry date
* last deployment status

### Manage a prototype

On the prototype details page, show:

* prototype name
* team
* owner
* deputy owner
* GitHub repository
* Azure resource group
* Azure web app name
* live URL
* status
* expiry date
* deployment history
* audit history

Allow the owner, Admin or Support to:

* change owner
* change deputy owner
* extend expiry date
* change password
* retry provisioning if failed
* archive prototype
* delete prototype

### Delete a prototype

Deleting a prototype should:

1. ask for confirmation
2. mark status as `Deleting`
3. create a deletion job
4. delete the Azure resource group
5. mark the prototype as `Deleted`
6. create an audit event

For local development, mock the delete operation.

## Domain model

Create these entities.

### Prototype

Fields:

```csharp
Guid Id
string Name
string Slug
string ServiceName
string Team
string Directorate
string OwnerEmail
string DeputyOwnerEmail
string GitHubRepositoryUrl
string AzureSubscriptionId
string AzureResourceGroupName
string AzureAppServicePlanName
string AzureWebAppName
string PrototypeUrl
PrototypeStatus Status
DateTimeOffset CreatedAt
string CreatedBy
DateTimeOffset ExpiresAt
DateTimeOffset? LastReviewedAt
bool ContainsRealUserData
bool IsProductionService
string Reason
string Region
string Sku
bool ExceptionRequired
```

Statuses:

```csharp
Requested
Queued
Provisioning
Live
Failed
Archived
Deleting
Deleted
Expired
```

### ProvisioningJob

Fields:

```csharp
Guid Id
Guid PrototypeId
ProvisioningJobType Type
ProvisioningJobStatus Status
DateTimeOffset CreatedAt
DateTimeOffset? StartedAt
DateTimeOffset? CompletedAt
int AttemptCount
string? ErrorMessage
```

Job types:

```csharp
Create
UpdatePassword
Delete
Retry
```

Job statuses:

```csharp
Queued
Running
Succeeded
Failed
```

### AuditEvent

Fields:

```csharp
Guid Id
Guid? PrototypeId
string ActorEmail
string Action
string Details
DateTimeOffset CreatedAt
```

### ExceptionRequest

Fields:

```csharp
Guid Id
Guid PrototypeId
string RequestedBy
string Reason
string RequestedSku
ExceptionRequestStatus Status
string? ApprovedBy
DateTimeOffset? ApprovedAt
DateTimeOffset CreatedAt
```

Statuses:

```csharp
Requested
Approved
Rejected
Cancelled
```

## Application services

Create interfaces in `PrototypeManager.Application`.

```csharp
public interface IPrototypeService
{
    Task<Prototype> CreateAsync(CreatePrototypeRequest request, string actorEmail);
    Task<Prototype?> GetAsync(Guid id);
    Task<IReadOnlyList<Prototype>> GetForUserAsync(string email);
    Task<IReadOnlyList<Prototype>> GetExpiringSoonAsync();
    Task ExtendExpiryAsync(Guid id, DateTimeOffset newExpiryDate, string actorEmail);
    Task ChangePasswordAsync(Guid id, string newPassword, string actorEmail);
    Task ArchiveAsync(Guid id, string actorEmail);
    Task DeleteAsync(Guid id, string actorEmail);
}
```

```csharp
public interface IPrototypeProvisioner
{
    Task<ProvisioningResult> CreatePrototypeAsync(Prototype prototype, string prototypePassword);
    Task<ProvisioningResult> UpdatePasswordAsync(Prototype prototype, string prototypePassword);
    Task DeletePrototypeAsync(Prototype prototype);
}
```

```csharp
public interface ICostControlPolicy
{
    Task ValidateCreateRequestAsync(CreatePrototypeRequest request);
    Task ValidatePrototypeAsync(Prototype prototype);
}
```

```csharp
public interface IAuditService
{
    Task RecordAsync(Guid? prototypeId, string actorEmail, string action, string details);
}
```

Implement two provisioners:

```text
MockPrototypeProvisioner
AzureBicepPrototypeProvisioner
```

Use `MockPrototypeProvisioner` in local development by default.

Use `AzureBicepPrototypeProvisioner` when configuration value `Provisioning:Mode` is set to `Azure`.

## Cost control rules

Implement these rules in `CostControlPolicy`.

Default allowed values:

```json
{
  "AllowedRegions": [ "uksouth" ],
  "AllowedSkus": [ "F1" ],
  "AllowedRuntime": "NODE|24-lts",
  "MaximumExpiryDays": 180,
  "RequireOwner": true,
  "RequireDeputyOwner": true,
  "RequireNoRealUserData": true,
  "RequireNotProduction": true
}
```

Validation rules:

* region must be allowed
* SKU must be F1 unless an exception is approved
* expiry date must be within 180 days unless an exception is approved
* owner email is required
* deputy owner email is required
* GitHub repository URL is required
* prototype must not contain real user data
* prototype must not be a production service
* generated Azure resource names must use the approved naming convention
* tags must include owner, deputy owner, team, expiry date and managedBy

## Azure naming convention

Generate names using a safe slug.

Rules:

* lower case
* letters, numbers and hyphens only
* no spaces
* no underscores
* trim to Azure-safe lengths
* add a short unique suffix

Use these patterns:

```text
Resource group:
rg-proto-{teamSlug}-{prototypeSlug}-{suffix}

App Service plan:
asp-proto-{teamSlug}-{prototypeSlug}-f1

Web app:
app-proto-{teamSlug}-{prototypeSlug}-{suffix}
```

## Required Azure tags

Apply these tags to every created Azure resource:

```text
purpose=prototype
managedBy=prototype-manager
environment=prototype
team={team}
owner={ownerEmail}
deputyOwner={deputyOwnerEmail}
expiresOn={yyyy-MM-dd}
prototypeId={prototypeId}
prototypeName={prototypeName}
costControl=free-tier
```

## Bicep template

Create `infra/prototype-hosting/main.bicep`.

It must create:

* Linux App Service plan
* Node.js Web App
* app settings
* HTTPS-only app
* FTPS disabled
* required tags

Use parameters:

```bicep
param location string
param appServicePlanName string
param webAppName string
param nodeRuntime string = 'NODE|24-lts'
param prototypePassword string
param tags object
```

The App Service plan must use:

```bicep
sku: {
  name: 'F1'
  tier: 'Free'
}
```

The Web App must set:

```bicep
httpsOnly: true
```

The Web App site config must set:

```bicep
linuxFxVersion: nodeRuntime
alwaysOn: false
ftpsState: 'Disabled'
```

App settings must include:

```text
NODE_ENV=production
PASSWORD={prototypePassword}
SCM_DO_BUILD_DURING_DEPLOYMENT=true
```

Include a README explaining how to compile the Bicep template to ARM JSON.

The web app must not require the Azure CLI at runtime. Compile the Bicep template as part of build or CI, then deploy the generated ARM JSON from the ASP.NET Core app.

## Azure provisioning implementation

In `AzureBicepPrototypeProvisioner`:

1. authenticate using `DefaultAzureCredential`
2. get the target subscription
3. create or update the resource group
4. deploy the compiled ARM template to the resource group
5. return resource names and prototype URL

Use configuration:

```json
{
  "Provisioning": {
    "Mode": "Mock",
    "AzureSubscriptionId": "",
    "Location": "uksouth",
    "CompiledTemplatePath": "infra/compiled/prototype-hosting.json"
  }
}
```

Do not store prototype passwords in the database as plain text.

For MVP:

* accept the password from the create form
* pass it to the provisioning job
* do not display it again
* for local development, use in-memory job payload storage

For the next iteration:

* store secrets in Azure Key Vault
* pass Key Vault references to App Service settings

## GitHub integration

For MVP, do not fully automate GitHub permissions.

Instead, generate a deployment workflow file that users can copy into their repository.

On the prototype details page, add a section called **Deployment workflow**.

Show a generated `.github/workflows/deploy-prototype.yml` file.

The generated workflow must:

* run on push to `main`
* use Node.js 24
* run `npm ci`
* run `npm test --if-present`
* deploy to the Azure Web App

For MVP, include a placeholder for authentication and explain that the organisation should configure GitHub OIDC.

Add this generated workflow as a textarea with a copy button.

Later, replace this with a GitHub App that creates a pull request automatically.

## Pages and routes

Create these pages.

```text
GET  /                         Dashboard
GET  /prototypes               List prototypes
GET  /prototypes/new           Create prototype form
POST /prototypes/new           Submit create prototype
GET  /prototypes/{id}          Prototype details
POST /prototypes/{id}/extend   Extend expiry
POST /prototypes/{id}/password Change password
POST /prototypes/{id}/archive  Archive prototype
POST /prototypes/{id}/delete   Delete prototype
POST /prototypes/{id}/retry    Retry failed provisioning
GET  /admin                    Admin dashboard
GET  /admin/exceptions         Exception requests
```

Use GOV.UK-style plain English content.

Use simple HTML first. Do not spend time on complex styling. Make the app usable and accessible.

## Forms

### Create prototype form

Fields:

```text
Prototype name
Service or project name
Team
Directorate
GitHub repository URL
Owner email
Deputy owner email
Expiry date
Reason for prototype
Prototype password
Confirmation: this prototype does not contain real user data
Confirmation: this is not a production service
```

Validation:

* all fields required
* expiry date must be in the future
* expiry date must not be more than 180 days away
* owner and deputy owner must be valid email addresses
* GitHub URL must be a valid GitHub repository URL
* both confirmation checkboxes must be checked
* password must be at least 12 characters

## Background jobs

Implement a simple hosted service that polls queued jobs every few seconds.

For MVP, this can run in the same ASP.NET Core app.

Create:

```text
ProvisioningWorker : BackgroundService
```

The worker should:

* find the oldest queued job
* mark it as running
* call `IPrototypeProvisioner`
* update the prototype
* update the job status
* write audit events
* handle errors safely

Do not allow infinite retries.

Maximum attempts: 3.

## Security requirements

* require authentication for all pages except health checks
* restrict admin pages to Admin role
* validate all user input
* never log prototype passwords
* never display prototype passwords after submission
* do not allow users to edit Azure resource names directly
* do not allow users to choose SKUs directly
* do not allow users to choose arbitrary regions
* do not allow users to enter arbitrary app settings
* use anti-forgery tokens on POST actions
* record audit events for create, update, retry, archive and delete actions

## Health checks

Add:

```text
GET /health
GET /health/ready
```

Health checks should report:

* app running
* database reachable
* provisioning mode configured

## Tests

Add tests for:

* slug generation
* cost control validation
* expiry date validation
* create prototype workflow
* failed provisioning workflow
* delete prototype workflow
* audit event creation

## Seed data

In Development environment, seed:

* 2 live prototypes
* 1 failed prototype
* 1 prototype expiring soon
* 1 archived prototype

This makes the dashboard useful during development.

## Acceptance criteria

The MVP is complete when:

1. I can run the app locally.
2. I can create a prototype in mock mode.
3. The app creates a prototype record.
4. The app queues a provisioning job.
5. The background worker processes the job.
6. The prototype moves from `Requested` to `Live`.
7. The app shows a fake Azure URL in mock mode.
8. The dashboard lists my prototypes.
9. I can view prototype details.
10. I can extend the expiry date.
11. I can archive a prototype.
12. I can delete a prototype in mock mode.
13. Failed provisioning jobs are visible.
14. Admins can retry failed jobs.
15. Audit events are recorded.
16. The Bicep template exists.
17. The Bicep template uses Free F1.
18. The Bicep template sets Node.js runtime.
19. The Bicep template sets `NODE_ENV=production`.
20. The Bicep template sets the prototype password as an app setting.
21. The Bicep template applies required tags.
22. The app has a clear interface boundary between mock provisioning and real Azure provisioning.
23. The app does not require direct user access to Azure.
24. The app does not allow users to create paid Azure resources.
25. The app includes a generated GitHub Actions workflow on the prototype details page.

## First implementation order

Build in this order:

1. create the solution and projects
2. add domain entities and enums
3. add EF Core DbContext
4. add create/list/detail pages
5. add mock provisioning worker
6. add audit events
7. add archive/delete/retry actions
8. add cost control validation
9. add Bicep template
10. add Azure provisioner interface
11. add Azure provisioner implementation
12. add generated GitHub Actions workflow
13. add tests
14. add README

## README content

Create a README with:

* what the app does
* how to run locally
* how mock provisioning works
* how Azure provisioning works
* required configuration
* how to compile the Bicep template
* security notes
* cost control notes
* known MVP limitations
* next steps

## MVP limitations to document

Document these limitations:

* GitHub workflow is generated but not automatically committed
* Azure provisioning requires configured managed identity or local Azure credentials
* prototype password is passed during provisioning but Key Vault is not implemented yet
* exception workflow is basic
* email reminders are not implemented yet
* Azure Policy is assumed to exist outside the app
* no Heroku migration wizard yet
* no custom domains
* no databases for prototypes
* no paid App Service plans in MVP

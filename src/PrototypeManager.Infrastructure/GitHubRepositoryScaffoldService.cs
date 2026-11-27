using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class MockGitHubRepositoryScaffoldService(
    IAuditService auditService,
    ILogger<MockGitHubRepositoryScaffoldService> logger) : IGitHubRepositoryScaffoldService
{
    public async Task ScaffoldAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Mock scaffold completed for prototype {PrototypeId} ({RepositoryUrl}).",
            prototype.Id,
            prototype.GitHubRepositoryUrl);

        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.GitHubCreateRepository);

        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.GitHubScaffoldKit);
    }
}

internal sealed class GitHubRepositoryScaffoldService(
    GitHubApiClient gitHubApiClient,
    GitHubTokenIdentityService gitHubTokenIdentityService,
    IAuditService auditService,
    IOptionsMonitor<GitHubOptions> githubOptions,
    ILogger<GitHubRepositoryScaffoldService> logger) : IGitHubRepositoryScaffoldService
{
    private GitHubOptions Options => githubOptions.CurrentValue;

    public async Task ScaffoldAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        var options = Options;
        var (owner, repository) = GitHubRepositoryValidator.ParseRepositoryUrl(prototype.GitHubRepositoryUrl);
        var repositoryExists = await RepositoryExistsAsync(owner, repository, cancellationToken);

        if (repositoryExists)
        {
            logger.LogInformation(
                "GitHub repository {Owner}/{Repository} already exists for prototype {PrototypeId}; skipping create and continuing scaffold.",
                owner,
                repository,
                prototype.Id);
        }
        else
        {
            logger.LogInformation(
                "Creating GitHub repository {Owner}/{Repository} for prototype {PrototypeId}.",
                owner,
                repository,
                prototype.Id);

            await GitHubRepositoryCreator.CreateAsync(
                gitHubApiClient,
                gitHubTokenIdentityService,
                options,
                logger,
                owner,
                repository,
                cancellationToken,
                allowExisting: true);
        }

        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.GitHubCreateRepository);

        // Installation tokens minted before a repo is created may not cover that repo yet.
        if (options.HasGitHubAppCredentials)
        {
            await gitHubApiClient.GetAccessTokenAsync(forceRefresh: true, cancellationToken);
        }

        await GitHubRepositoryCollaboratorAssigner.AssignConfiguredAsync(
            gitHubApiClient,
            options,
            logger,
            owner,
            repository,
            cancellationToken);

        logger.LogInformation(
            "Scaffolding GOV.UK Prototype Kit in {Owner}/{Repository}.",
            owner,
            repository);

        await ScaffoldPrototypeKitAsync(owner, repository, cancellationToken);

        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.GitHubScaffoldKit);
    }

    private async Task<bool> RepositoryExistsAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        var baseUrl = Options.ApiBaseUrl.TrimEnd('/');
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}",
            cancellationToken: cancellationToken);

        return response.IsSuccessStatusCode;
    }

    private async Task ScaffoldPrototypeKitAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        var options = Options;
        var files = await PrototypeKitTarballScaffold.LoadStarterFilesAsync(
            options.PrototypeKitVersion,
            logger,
            cancellationToken);

        var branch = string.IsNullOrWhiteSpace(options.WorkflowBranch)
            ? "main"
            : options.WorkflowBranch.Trim();

        await GitHubGitDataCommitPublisher.PublishAsync(
            gitHubApiClient,
            options,
            logger,
            owner,
            repository,
            files,
            "Initial commit from GOV.UK Prototype Kit",
            branch,
            cancellationToken);
    }
}

internal sealed class GitHubRepositoryScaffoldServiceSelector(
    MockGitHubRepositoryScaffoldService mockService,
    GitHubRepositoryScaffoldService liveService,
    IOptions<GitHubOptions> githubOptions) : IGitHubRepositoryScaffoldService
{
    private IGitHubRepositoryScaffoldService Implementation =>
        githubOptions.Value.IsConfigured
            ? liveService
            : mockService;

    public Task ScaffoldAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
        Implementation.ScaffoldAsync(prototype, cancellationToken);
}

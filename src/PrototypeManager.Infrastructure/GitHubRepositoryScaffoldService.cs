using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
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
    IOptions<GitHubOptions> githubOptions,
    ILogger<GitHubRepositoryScaffoldService> logger) : IGitHubRepositoryScaffoldService
{
    private readonly GitHubOptions _githubOptions = githubOptions.Value;

    public async Task ScaffoldAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        var token = gitHubApiClient.RequireToken();
        var (owner, repository) = GitHubRepositoryValidator.ParseRepositoryUrl(prototype.GitHubRepositoryUrl);

        logger.LogInformation(
            "Creating GitHub repository {Owner}/{Repository} for prototype {PrototypeId}.",
            owner,
            repository,
            prototype.Id);

        await GitHubRepositoryCreator.CreateAsync(
            gitHubApiClient,
            gitHubTokenIdentityService,
            _githubOptions,
            logger,
            owner,
            repository,
            cancellationToken,
            allowExisting: true);

        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.GitHubCreateRepository);

        logger.LogInformation(
            "Scaffolding GOV.UK Prototype Kit in {Owner}/{Repository}.",
            owner,
            repository);

        await ScaffoldPrototypeKitAsync(owner, repository, token, cancellationToken);

        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.GitHubScaffoldKit);
    }

    private async Task ScaffoldPrototypeKitAsync(
        string owner,
        string repository,
        string token,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"pm-kit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            await WritePrototypeKitStarterFilesAsync(workingDirectory, cancellationToken);
            await PushInitialCommitAsync(
                GitHubRepositoryCreator.ResolveGitHost(_githubOptions),
                owner,
                repository,
                token,
                workingDirectory,
                cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Unable to delete temporary scaffold directory {Directory}.", workingDirectory);
            }
        }
    }

    private Task WritePrototypeKitStarterFilesAsync(string workingDirectory, CancellationToken cancellationToken) =>
        PrototypeKitTarballScaffold.WriteStarterFilesAsync(
            workingDirectory,
            _githubOptions.PrototypeKitVersion,
            logger,
            cancellationToken);

    private static async Task PushInitialCommitAsync(
        string gitHost,
        string owner,
        string repository,
        string token,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var remoteUrl = $"https://x-access-token:{token}@{gitHost}/{owner}/{repository}.git";

        await RunGitAsync(["init", "-b", "main"], workingDirectory, cancellationToken);
        await RunGitAsync(["config", "user.email", "prototype-manager@education.gov.uk"], workingDirectory, cancellationToken);
        await RunGitAsync(["config", "user.name", "Prototype Manager"], workingDirectory, cancellationToken);
        await RunGitAsync(["add", "."], workingDirectory, cancellationToken);
        await RunGitAsync(["commit", "-m", "Initial commit from GOV.UK Prototype Kit"], workingDirectory, cancellationToken);
        await RunGitAsync(["remote", "add", "origin", remoteUrl], workingDirectory, cancellationToken);
        await RunGitAsync(["push", "-u", "origin", "main"], workingDirectory, cancellationToken);
    }

    private static async Task RunGitAsync(string[] arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var exitCode = await RunProcessAsync("git", string.Join(' ', arguments.Select(QuoteProcessArgument)), workingDirectory, cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException("Unable to push GOV.UK Prototype Kit code to GitHub.");
        }
    }

    private static async Task<int> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start {fileName}.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException(error.Trim());
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            Debug.WriteLine(output);
        }

        return process.ExitCode;
    }

    private static string QuoteProcessArgument(string value) =>
        value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}

internal sealed class GitHubRepositoryScaffoldServiceSelector(
    MockGitHubRepositoryScaffoldService mockService,
    GitHubRepositoryScaffoldService liveService,
    IOptions<GitHubOptions> githubOptions) : IGitHubRepositoryScaffoldService
{
    private IGitHubRepositoryScaffoldService Implementation =>
        string.IsNullOrWhiteSpace(githubOptions.Value.PersonalAccessToken)
            ? mockService
            : liveService;

    public Task ScaffoldAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
        Implementation.ScaffoldAsync(prototype, cancellationToken);
}

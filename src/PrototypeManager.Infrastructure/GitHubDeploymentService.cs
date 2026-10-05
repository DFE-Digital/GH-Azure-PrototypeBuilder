using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using Sodium;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubDeploymentService(
    GitHubApiClient gitHubApiClient,
    IGitHubAccessValidator gitHubAccessValidator,
    IDeploymentWorkflowGenerator workflowGenerator,
    IPrototypeSiteVerifier prototypeSiteVerifier,
    IAuditService auditService,
    IOptions<GitHubOptions> githubOptions,
    IOptions<ProvisioningOptions> provisioningOptions,
    ILogger<GitHubDeploymentService> logger) : IGitHubDeploymentService
{
    private readonly GitHubOptions _githubOptions = githubOptions.Value;
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;

    public async Task<GitHubDeploymentResult> ConfigureAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        _ = gitHubApiClient.RequireToken();

        if (string.IsNullOrWhiteSpace(prototype.AzureWebAppName) ||
            string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName))
        {
            throw new ValidationException("Azure hosting must be provisioned before GitHub deployment can be configured.");
        }

        await gitHubAccessValidator.ValidateForRepositoryAsync(prototype.GitHubRepositoryUrl, cancellationToken);

        var (owner, repository) = GitHubRepositoryValidator.ParseRepositoryUrl(prototype.GitHubRepositoryUrl);
        var (_, workflowBranch) = await GitHubRepositoryMetadata.ResolveWorkflowBranchAsync(
            gitHubApiClient,
            _githubOptions,
            owner,
            repository,
            cancellationToken);

        if (!string.Equals(workflowBranch, _githubOptions.WorkflowBranch, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Using GitHub branch {WorkflowBranch} for prototype {PrototypeId} (configured branch {ConfiguredBranch} was not found).",
                workflowBranch,
                prototype.Id,
                _githubOptions.WorkflowBranch);
        }

        var workflowPath = $".github/workflows/{_githubOptions.WorkflowFileName}";
        var workflowContent = workflowGenerator.Generate(
            prototype,
            new DeploymentWorkflowOptions { UsePublishProfile = true });

        logger.LogInformation(
            "Configuring GitHub deployment for prototype {PrototypeId} in {Owner}/{Repository}.",
            prototype.Id,
            owner,
            repository);

        var publishProfile = await GetPublishProfileAsync(prototype, cancellationToken);

        await UpsertRepositorySecretAsync(
            owner,
            repository,
            _githubOptions.PublishProfileSecretName,
            publishProfile,
            cancellationToken);

        var commitUrl = await UpsertWorkflowFileAsync(
            owner,
            repository,
            workflowBranch ?? _githubOptions.WorkflowBranch,
            workflowPath,
            workflowContent,
            cancellationToken);

        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.GitHubConfigure);

        string? workflowRunUrl = null;

        if (_githubOptions.TriggerInitialDeployment)
        {
            workflowRunUrl = await TriggerWorkflowAsync(
                owner,
                repository,
                workflowBranch ?? _githubOptions.WorkflowBranch,
                cancellationToken);

            await ProvisioningTaskRecorder.RecordCompletedAsync(
                auditService,
                logger,
                prototype.Id,
                prototype.Slug,
                ProvisioningTasks.GitHubDeploy);

            await prototypeSiteVerifier.WaitForDeployedContentAsync(
                prototype.Id,
                prototype.Slug,
                prototype.PrototypeUrl,
                auditService,
                cancellationToken);
        }

        return new GitHubDeploymentResult
        {
            Succeeded = true,
            WorkflowFilePath = workflowPath,
            CommitUrl = commitUrl,
            WorkflowRunUrl = workflowRunUrl,
            Message = _githubOptions.TriggerInitialDeployment
                ? "GitHub workflow committed, publish profile secret saved, initial deployment triggered, and prototype URL verified."
                : "GitHub workflow committed and publish profile secret saved."
        };
    }

    private async Task<string> GetPublishProfileAsync(Prototype prototype, CancellationToken cancellationToken)
    {
        var subscriptionId = string.IsNullOrWhiteSpace(prototype.AzureSubscriptionId)
            ? _provisioningOptions.AzureSubscriptionId
            : prototype.AzureSubscriptionId;

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            throw new InvalidOperationException("Azure subscription ID is not configured.");
        }

        var armClient = new ArmClient(new DefaultAzureCredential());
        var subscription = armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));
        var resourceGroup = await subscription.GetResourceGroups().GetAsync(prototype.AzureResourceGroupName, cancellationToken);
        var webSite = await resourceGroup.Value.GetWebSites().GetAsync(prototype.AzureWebAppName, cancellationToken);
        var response = await webSite.Value.GetPublishingProfileXmlWithSecretsAsync(new CsmPublishingProfile(), cancellationToken);
        await using var publishProfileStream = response.Value;
        var publishProfileXml = PublishProfileReader.ReadXml(publishProfileStream);

        return PublishProfileNormalizer.NormalizeForGitHubActions(publishProfileXml, prototype.AzureWebAppName);
    }

    private async Task<string> UpsertWorkflowFileAsync(
        string owner,
        string repository,
        string workflowBranch,
        string workflowPath,
        string workflowContent,
        CancellationToken cancellationToken)
    {
        var baseUrl = _githubOptions.ApiBaseUrl.TrimEnd('/');
        var encodedPath = Uri.EscapeDataString(workflowPath);
        var existingSha = await GetExistingFileShaAsync(
            baseUrl,
            owner,
            repository,
            workflowPath,
            workflowBranch,
            cancellationToken);

        var payload = new Dictionary<string, object>
        {
            ["message"] = "Configure automated prototype deployment via Prototype Manager",
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(workflowContent)),
            ["branch"] = workflowBranch
        };

        if (!string.IsNullOrWhiteSpace(existingSha))
        {
            payload["sha"] = existingSha;
        }

        using var requestContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Put,
            $"{baseUrl}/repos/{owner}/{repository}/contents/{encodedPath}",
            requestContent,
            cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(response, "commit GitHub workflow file", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("commit", out var commit) &&
            commit.TryGetProperty("html_url", out var htmlUrl))
        {
            return htmlUrl.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private async Task<string?> GetExistingFileShaAsync(
        string baseUrl,
        string owner,
        string repository,
        string workflowPath,
        string branch,
        CancellationToken cancellationToken)
    {
        var encodedPath = Uri.EscapeDataString(workflowPath);
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}/contents/{encodedPath}?ref={Uri.EscapeDataString(branch)}",
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await gitHubApiClient.EnsureSuccessAsync(response, "read existing workflow file", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("sha", out var sha)
            ? sha.GetString()
            : null;
    }

    private async Task UpsertRepositorySecretAsync(
        string owner,
        string repository,
        string secretName,
        string secretValue,
        CancellationToken cancellationToken)
    {
        var baseUrl = _githubOptions.ApiBaseUrl.TrimEnd('/');
        using var keyResponse = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}/actions/secrets/public-key",
            cancellationToken: cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(
            keyResponse,
            "read GitHub Actions public key",
            cancellationToken);

        await using var keyStream = await keyResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var keyDocument = await JsonDocument.ParseAsync(keyStream, cancellationToken: cancellationToken);
        var keyId = keyDocument.RootElement.GetProperty("key_id").GetString()
            ?? throw new InvalidOperationException("GitHub Actions public key response did not include key_id.");
        var publicKey = keyDocument.RootElement.GetProperty("key").GetString()
            ?? throw new InvalidOperationException("GitHub Actions public key response did not include key.");

        var encryptedValue = EncryptSecret(secretValue, publicKey);
        var payload = JsonSerializer.Serialize(new
        {
            encrypted_value = encryptedValue,
            key_id = keyId
        });

        using var requestContent = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Put,
            $"{baseUrl}/repos/{owner}/{repository}/actions/secrets/{Uri.EscapeDataString(secretName)}",
            requestContent,
            cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(response, $"save GitHub secret {secretName}", cancellationToken);
    }

    private static string EncryptSecret(string secret, string publicKeyBase64)
    {
        var publicKey = Convert.FromBase64String(publicKeyBase64);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var encrypted = SealedPublicKeyBox.Create(secretBytes, publicKey);

        return Convert.ToBase64String(encrypted);
    }

    private async Task<string?> TriggerWorkflowAsync(
        string owner,
        string repository,
        string workflowBranch,
        CancellationToken cancellationToken)
    {
        var baseUrl = _githubOptions.ApiBaseUrl.TrimEnd('/');
        var payload = JsonSerializer.Serialize(new { @ref = workflowBranch });
        using var requestContent = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Post,
            $"{baseUrl}/repos/{owner}/{repository}/actions/workflows/{Uri.EscapeDataString(_githubOptions.WorkflowFileName)}/dispatches",
            requestContent,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "GitHub workflow dispatch failed for {Owner}/{Repository}: {StatusCode} {Body}",
                owner,
                repository,
                (int)response.StatusCode,
                body);

            return null;
        }

        return $"https://github.com/{owner}/{repository}/actions";
    }
}

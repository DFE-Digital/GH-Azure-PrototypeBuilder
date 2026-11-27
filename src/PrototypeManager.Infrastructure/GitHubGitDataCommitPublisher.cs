using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

/// <summary>
/// Publishes an initial commit via the Git Data API.
/// Prefer this over <c>git push</c> for GitHub App installation tokens, which often cannot use HTTPS git write.
/// </summary>
internal static class GitHubGitDataCommitPublisher
{
    public static async Task PublishAsync(
        GitHubApiClient gitHubApiClient,
        GitHubOptions githubOptions,
        ILogger logger,
        string owner,
        string repository,
        IReadOnlyDictionary<string, byte[]> files,
        string commitMessage,
        string branch,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("No files were provided for the initial commit.");
        }

        var baseUrl = $"{githubOptions.ApiBaseUrl.TrimEnd('/')}/repos/{owner}/{repository}";

        // Git Data blobs/trees fail on completely empty repositories (HTTP 409).
        // auto_init / Contents API seeding creates the first commit that Git Data needs.
        var parentSha = await WaitForBranchHeadShaAsync(
            gitHubApiClient,
            baseUrl,
            branch,
            cancellationToken);

        if (parentSha is null)
        {
            parentSha = await SeedEmptyRepositoryAsync(
                gitHubApiClient,
                baseUrl,
                branch,
                logger,
                owner,
                repository,
                cancellationToken);
        }

        var treeItems = new List<object>(files.Count);

        foreach (var (path, contents) in files.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var normalisedPath = path.Replace('\\', '/').TrimStart('/');
            using var blobResponse = await gitHubApiClient.SendAsync(
                HttpMethod.Post,
                $"{baseUrl}/git/blobs",
                JsonContent.Create(new
                {
                    content = Convert.ToBase64String(contents),
                    encoding = "base64"
                }),
                cancellationToken);

            await gitHubApiClient.EnsureSuccessAsync(
                blobResponse,
                $"create blob for {normalisedPath}",
                cancellationToken);

            await using var blobStream = await blobResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var blobDocument = await JsonDocument.ParseAsync(blobStream, cancellationToken: cancellationToken);
            var blobSha = blobDocument.RootElement.GetProperty("sha").GetString()
                ?? throw new InvalidOperationException($"GitHub did not return a blob SHA for {normalisedPath}.");

            treeItems.Add(new
            {
                path = normalisedPath,
                mode = "100644",
                type = "blob",
                sha = blobSha
            });
        }

        using var treeResponse = await gitHubApiClient.SendAsync(
            HttpMethod.Post,
            $"{baseUrl}/git/trees",
            JsonContent.Create(new { tree = treeItems }),
            cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(treeResponse, "create git tree", cancellationToken);

        await using var treeStream = await treeResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var treeDocument = await JsonDocument.ParseAsync(treeStream, cancellationToken: cancellationToken);
        var treeSha = treeDocument.RootElement.GetProperty("sha").GetString()
            ?? throw new InvalidOperationException("GitHub did not return a tree SHA.");

        using var commitResponse = await gitHubApiClient.SendAsync(
            HttpMethod.Post,
            $"{baseUrl}/git/commits",
            JsonContent.Create(new
            {
                message = commitMessage,
                tree = treeSha,
                parents = new[] { parentSha }
            }),
            cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(commitResponse, "create git commit", cancellationToken);

        await using var commitStream = await commitResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var commitDocument = await JsonDocument.ParseAsync(commitStream, cancellationToken: cancellationToken);
        var commitSha = commitDocument.RootElement.GetProperty("sha").GetString()
            ?? throw new InvalidOperationException("GitHub did not return a commit SHA.");

        using var updateRefResponse = await gitHubApiClient.SendAsync(
            new HttpMethod("PATCH"),
            $"{baseUrl}/git/refs/heads/{Uri.EscapeDataString(branch)}",
            JsonContent.Create(new
            {
                sha = commitSha,
                force = true
            }),
            cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(updateRefResponse, "update branch ref", cancellationToken);

        logger.LogInformation(
            "Published commit {CommitSha} to {Owner}/{Repository}:{Branch} via GitHub API.",
            commitSha,
            owner,
            repository,
            branch);
    }

    private static async Task<string> SeedEmptyRepositoryAsync(
        GitHubApiClient gitHubApiClient,
        string repositoryBaseUrl,
        string branch,
        ILogger logger,
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Repository {Owner}/{Repository} has no commits yet; seeding an initial file so Git Data API scaffolding can proceed.",
            owner,
            repository);

        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Put,
            $"{repositoryBaseUrl}/contents/.prototype-manager-init",
            JsonContent.Create(new
            {
                message = "Initialize repository for Prototype Manager scaffolding",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes("initialized\n")),
                branch
            }),
            cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(response, "seed empty repository", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var commitSha = document.RootElement.TryGetProperty("commit", out var commitElement) &&
                        commitElement.TryGetProperty("sha", out var shaElement)
            ? shaElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(commitSha))
        {
            commitSha = await TryGetBranchHeadShaAsync(gitHubApiClient, repositoryBaseUrl, branch, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(commitSha))
        {
            throw new InvalidOperationException(
                "Failed to initialise the empty GitHub repository before scaffolding the Prototype Kit.");
        }

        return commitSha;
    }

    private static async Task<string?> WaitForBranchHeadShaAsync(
        GitHubApiClient gitHubApiClient,
        string repositoryBaseUrl,
        string branch,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var sha = await TryGetBranchHeadShaAsync(
                gitHubApiClient,
                repositoryBaseUrl,
                branch,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(sha))
            {
                return sha;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return null;
    }

    private static async Task<string?> TryGetBranchHeadShaAsync(
        GitHubApiClient gitHubApiClient,
        string repositoryBaseUrl,
        string branch,
        CancellationToken cancellationToken)
    {
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{repositoryBaseUrl}/git/ref/heads/{Uri.EscapeDataString(branch)}",
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("object", out var objectElement) &&
               objectElement.TryGetProperty("sha", out var shaElement)
            ? shaElement.GetString()
            : null;
    }
}

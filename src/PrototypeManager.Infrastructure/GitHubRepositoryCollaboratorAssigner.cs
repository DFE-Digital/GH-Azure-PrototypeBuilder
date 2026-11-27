using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal static class GitHubRepositoryCollaboratorAssigner
{
    public static async Task AssignConfiguredAsync(
        GitHubApiClient gitHubApiClient,
        GitHubOptions githubOptions,
        ILogger logger,
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        var usernames = (githubOptions.DefaultCollaborators ?? [])
            .Select(username => username?.Trim())
            .Where(username => !string.IsNullOrWhiteSpace(username))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        if (usernames.Count == 0)
        {
            logger.LogWarning(
                "GitHub:DefaultCollaborators is empty; no collaborators will be assigned on {Owner}/{Repository}.",
                owner,
                repository);
            return;
        }

        var permission = string.IsNullOrWhiteSpace(githubOptions.DefaultCollaboratorPermission)
            ? "admin"
            : githubOptions.DefaultCollaboratorPermission.Trim().ToLowerInvariant();

        logger.LogInformation(
            "Assigning GitHub collaborators ({Permission}) on {Owner}/{Repository}: {Usernames}.",
            permission,
            owner,
            repository,
            string.Join(", ", usernames));

        foreach (var username in usernames)
        {
            await AssignAsync(
                gitHubApiClient,
                githubOptions,
                logger,
                owner,
                repository,
                username,
                permission,
                cancellationToken);
        }
    }

    private static async Task AssignAsync(
        GitHubApiClient gitHubApiClient,
        GitHubOptions githubOptions,
        ILogger logger,
        string owner,
        string repository,
        string username,
        string permission,
        CancellationToken cancellationToken)
    {
        var baseUrl = githubOptions.ApiBaseUrl.TrimEnd('/');
        var collaboratorUrl =
            $"{baseUrl}/repos/{owner}/{repository}/collaborators/{Uri.EscapeDataString(username)}";

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(new { permission }),
            Encoding.UTF8,
            "application/json");

        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Put,
            collaboratorUrl,
            requestContent,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var acceptedPermissions = ReadAcceptedPermissions(response);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            await EnsureCollaboratorVisibleAsync(
                gitHubApiClient,
                logger,
                baseUrl,
                owner,
                repository,
                username,
                permission,
                cancellationToken);

            logger.LogInformation(
                "Granted {Username} direct {Permission} access on {Owner}/{Repository}.",
                username,
                permission,
                owner,
                repository);
            return;
        }

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var invitationUrl = TryReadInvitationHtmlUrl(body);
            logger.LogInformation(
                "Invited {Username} as {Permission} on {Owner}/{Repository}. They must accept the GitHub invitation{InvitationDetail}.",
                username,
                permission,
                owner,
                repository,
                string.IsNullOrWhiteSpace(invitationUrl) ? string.Empty : $": {invitationUrl}");
            return;
        }

        var githubMessage = GitHubApiErrors.ReadMessage(body);
        var detail = string.IsNullOrWhiteSpace(githubMessage)
            ? string.Empty
            : $" GitHub said: {githubMessage}";
        var accepted = string.IsNullOrWhiteSpace(acceptedPermissions)
            ? string.Empty
            : $" GitHub requires: {acceptedPermissions}.";

        throw new ValidationException(
            $"Unable to assign GitHub user '{username}' to {owner}/{repository} ({(int)response.StatusCode}).{detail}{accepted} " +
            "The GitHub App needs Repository permission Administration: Read and write. " +
            "If the user is outside the organisation, org/enterprise policy must allow outside collaborators, " +
            "and the user must accept the invitation. Prefer an organisation member with admin access.");
    }

    private static async Task EnsureCollaboratorVisibleAsync(
        GitHubApiClient gitHubApiClient,
        ILogger logger,
        string baseUrl,
        string owner,
        string repository,
        string username,
        string permission,
        CancellationToken cancellationToken)
    {
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}/collaborators/{Uri.EscapeDataString(username)}/permission",
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Granted access to {Username} on {Owner}/{Repository} but could not verify permission (HTTP {StatusCode}).",
                username,
                owner,
                repository,
                (int)response.StatusCode);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var actual = document.RootElement.TryGetProperty("permission", out var permissionElement)
                ? permissionElement.GetString()
                : null;

            logger.LogInformation(
                "Verified {Username} has '{ActualPermission}' on {Owner}/{Repository} (requested {RequestedPermission}).",
                username,
                actual ?? "unknown",
                owner,
                repository,
                permission);
        }
        catch (JsonException)
        {
            logger.LogInformation(
                "Verified {Username} is listed on {Owner}/{Repository}.",
                username,
                owner,
                repository);
        }
    }

    private static string? TryReadInvitationHtmlUrl(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("html_url", out var htmlUrl))
            {
                return htmlUrl.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore non-JSON invitation payloads
        }

        return null;
    }

    private static string? ReadAcceptedPermissions(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Accepted-GitHub-Permissions", out var values))
        {
            return null;
        }

        var joined = string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}

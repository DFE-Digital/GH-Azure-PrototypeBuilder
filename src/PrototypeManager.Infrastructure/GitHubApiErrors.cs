using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;

namespace PrototypeManager.Infrastructure;

internal static class GitHubApiErrors
{
    public static string ReadMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);

            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return responseBody.Trim();
        }

        return responseBody.Trim();
    }

    public static ValidationException CreateValidationException(
        HttpStatusCode statusCode,
        string operation,
        string? responseBody,
        string? acceptedPermissions = null)
    {
        var githubMessage = ReadMessage(responseBody);
        var detail = string.IsNullOrWhiteSpace(githubMessage)
            ? string.Empty
            : $" GitHub said: {githubMessage}";
        var accepted = string.IsNullOrWhiteSpace(acceptedPermissions)
            ? string.Empty
            : $" GitHub requires: {acceptedPermissions}.";

        var guidance = statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "Create a new fine-grained token with access to the repository, then restart the app.",
            HttpStatusCode.NotFound =>
                "Check the repository URL, branch name, and that the token includes this repository.",
            HttpStatusCode.Forbidden when IsSecretOperation(operation) =>
                "The GitHub App needs Repository permission Secrets: Read and write. " +
                "After changing App permissions, accept the update on the organisation installation, then retry.",
            HttpStatusCode.Forbidden when operation.Contains("workflow", StringComparison.OrdinalIgnoreCase) =>
                "GitHub requires a separate Workflows permission (not Actions) to create files in .github/workflows/. " +
                "On the GitHub App, set Workflows: Read and write, accept the installation update, then retry.",
            HttpStatusCode.Forbidden when githubMessage.Contains("not accessible by integration", StringComparison.OrdinalIgnoreCase) =>
                "GitHub App installation tokens cannot call that API. Grant the missing App permission, " +
                "accept the permission update on the organisation installation, then retry.",
            HttpStatusCode.Forbidden when operation.Contains("blob", StringComparison.OrdinalIgnoreCase) ||
                                          operation.Contains("tree", StringComparison.OrdinalIgnoreCase) ||
                                          operation.Contains("commit", StringComparison.OrdinalIgnoreCase) ||
                                          operation.Contains("ref", StringComparison.OrdinalIgnoreCase) =>
                "The GitHub App needs Contents: Read and write (Repository permissions) on the organisation installation.",
            HttpStatusCode.Forbidden =>
                "Check the GitHub App has Contents, Workflows, and Secrets read/write for this repository, " +
                "and that the organisation has accepted any pending permission updates.",
            _ => "Check the GitHub App/token permissions and repository access."
        };

        return new ValidationException(
            $"GitHub denied access for {operation} ({(int)statusCode}).{detail}{accepted} {guidance}");
    }

    private static bool IsSecretOperation(string operation) =>
        operation.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        operation.Contains("Actions secrets", StringComparison.OrdinalIgnoreCase);

    public static ValidationException CreateRepositoryException(
        HttpStatusCode statusCode,
        string organisation,
        string? responseBody,
        bool usingGitHubApp = false)
    {
        var githubMessage = ReadMessage(responseBody);
        var detail = string.IsNullOrWhiteSpace(githubMessage)
            ? string.Empty
            : $" GitHub said: {githubMessage}";

        var guidance = statusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Forbidden when usingGitHubApp =>
                $"Confirm the GitHub App is installed on {organisation} with permission to create repositories " +
                "(Administration: Read and write, or Contents + Metadata as required by your app settings).",
            HttpStatusCode.NotFound or HttpStatusCode.Forbidden =>
                $"The token must be authorized for the {organisation} organisation and have permission to create repositories there. " +
                "For fine-grained tokens, open the token settings, add access to that organisation, and grant Repository administration (read and write). " +
                "For classic tokens, ensure the token has repo scope and your GitHub account can create repositories in the organisation.",
            _ => "Check the GitHub App/token permissions and organisation access."
        };

        return new ValidationException(
            $"Unable to create a GitHub repository in {organisation} ({(int)statusCode}).{detail} {guidance}");
    }
}

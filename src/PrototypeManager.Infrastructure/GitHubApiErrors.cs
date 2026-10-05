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
        string? responseBody)
    {
        var githubMessage = ReadMessage(responseBody);
        var detail = string.IsNullOrWhiteSpace(githubMessage)
            ? string.Empty
            : $" GitHub said: {githubMessage}";

        var guidance = statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "Create a new fine-grained token with access to the repository, then restart the app.",
            HttpStatusCode.NotFound =>
                "Check the repository URL, branch name, and that the token includes this repository.",
            HttpStatusCode.Forbidden when operation.Contains("workflow", StringComparison.OrdinalIgnoreCase) =>
                "GitHub requires a separate Workflows permission (not Actions) to create files in .github/workflows/. " +
                "On your fine-grained token, click + Add permissions, choose Workflows, set Read and write, then Update the token and restart the app.",
            HttpStatusCode.Forbidden =>
                "Check the token has Contents, Workflows, and Secrets read/write for this repository.",
            _ => "Check the token permissions and repository access."
        };

        return new ValidationException(
            $"GitHub denied access for {operation} ({(int)statusCode}).{detail} {guidance}");
    }

    public static ValidationException CreateRepositoryException(
        HttpStatusCode statusCode,
        string organisation,
        string? responseBody)
    {
        var githubMessage = ReadMessage(responseBody);
        var detail = string.IsNullOrWhiteSpace(githubMessage)
            ? string.Empty
            : $" GitHub said: {githubMessage}";

        var guidance = statusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Forbidden =>
                $"The token must be authorized for the {organisation} organisation and have permission to create repositories there. " +
                "For fine-grained tokens, open the token settings, add access to that organisation, and grant Repository administration (read and write). " +
                "For classic tokens, ensure the token has repo scope and your GitHub account can create repositories in the organisation.",
            _ => "Check the token permissions and organisation access."
        };

        return new ValidationException(
            $"Unable to create a GitHub repository in {organisation} ({(int)statusCode}).{detail} {guidance}");
    }
}

namespace PrototypeManager.Infrastructure;

public static class GitHubTokenNormalizer
{
    public static string Normalize(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        return new string(token.Where(character => !char.IsWhiteSpace(character)).ToArray());
    }
}

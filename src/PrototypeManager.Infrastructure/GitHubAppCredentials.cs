using System.Text;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal static class GitHubAppCredentials
{
    public static string ResolvePrivateKeyPem(GitHubOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKey))
        {
            return NormalisePem(options.PrivateKey);
        }

        if (string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            return string.Empty;
        }

        var path = ResolveExistingPath(options.PrivateKeyPath.Trim());
        if (path is null)
        {
            throw new FileNotFoundException(
                $"GitHub App private key file was not found at '{options.PrivateKeyPath}'.",
                options.PrivateKeyPath);
        }

        var pem = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                $"GitHub App private key file at '{path}' is empty. Paste the PEM contents into that file.");
        }

        return NormalisePem(pem);
    }

    private static string? ResolveExistingPath(string path)
    {
        var candidates = new List<string>();

        if (Path.IsPathRooted(path))
        {
            candidates.Add(path);
        }
        else
        {
            candidates.Add(Path.GetFullPath(path));
            candidates.Add(Path.GetFullPath(path, Directory.GetCurrentDirectory()));
            candidates.Add(Path.GetFullPath(path, AppContext.BaseDirectory));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string NormalisePem(string value)
    {
        var pem = value.Trim().Replace("\\n", "\n", StringComparison.Ordinal);

        // Support keys pasted as a single line without escaped newlines.
        if (!pem.Contains('\n', StringComparison.Ordinal) &&
            pem.Contains("-----BEGIN", StringComparison.Ordinal) &&
            pem.Contains("-----END", StringComparison.Ordinal))
        {
            pem = pem
                .Replace("-----BEGIN RSA PRIVATE KEY-----", "-----BEGIN RSA PRIVATE KEY-----\n", StringComparison.Ordinal)
                .Replace("-----END RSA PRIVATE KEY-----", "\n-----END RSA PRIVATE KEY-----", StringComparison.Ordinal)
                .Replace("-----BEGIN PRIVATE KEY-----", "-----BEGIN PRIVATE KEY-----\n", StringComparison.Ordinal)
                .Replace("-----END PRIVATE KEY-----", "\n-----END PRIVATE KEY-----", StringComparison.Ordinal);
        }

        return pem.Trim();
    }

    public static string CreateAppJwt(long appId, string privateKeyPem)
    {
        var now = DateTimeOffset.UtcNow;
        var headerJson = """{"alg":"RS256","typ":"JWT"}""";
        var payloadJson = $"{{\"iat\":{now.AddSeconds(-60).ToUnixTimeSeconds()},\"exp\":{now.AddMinutes(9).ToUnixTimeSeconds()},\"iss\":{appId}}}";

        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var unsigned = $"{header}.{payload}";

        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(unsigned),
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        return $"{unsigned}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

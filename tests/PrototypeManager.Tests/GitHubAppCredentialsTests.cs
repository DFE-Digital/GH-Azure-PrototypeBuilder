using System.Security.Cryptography;
using PrototypeManager.Application;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class GitHubAppCredentialsTests
{
    [Fact]
    public void CreateAppJwt_ProducesThreePartToken()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();

        var jwt = GitHubAppCredentials.CreateAppJwt(5934990, pem);
        var parts = jwt.Split('.');

        Assert.Equal(3, parts.Length);
        Assert.False(string.IsNullOrWhiteSpace(parts[0]));
        Assert.False(string.IsNullOrWhiteSpace(parts[1]));
        Assert.False(string.IsNullOrWhiteSpace(parts[2]));
    }

    [Fact]
    public void GitHubOptions_IsConfigured_WhenAppCredentialsPresent()
    {
        var options = new GitHubOptions
        {
            AppId = 5934990,
            InstallationId = 186088276,
            PrivateKey = "-----BEGIN RSA PRIVATE KEY-----\nABC\n-----END RSA PRIVATE KEY-----"
        };

        Assert.True(options.HasGitHubAppCredentials);
        Assert.True(options.IsConfigured);
    }
}

using PrototypeManager.Application;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class GitHubRepositoryCreatorTests
{
    [Theory]
    [InlineData("https://api.github.com", "github.com")]
    [InlineData("https://api.github.com/", "github.com")]
    [InlineData("https://api.ghe.example.com", "ghe.example.com")]
    public void ResolveGitHost_ReturnsExpectedHost(string apiBaseUrl, string expectedHost)
    {
        var host = GitHubRepositoryCreator.ResolveGitHost(new GitHubOptions
        {
            ApiBaseUrl = apiBaseUrl
        });

        Assert.Equal(expectedHost, host);
    }
}

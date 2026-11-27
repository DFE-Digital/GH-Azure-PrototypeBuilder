using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class GitHubPrototypeCodebaseAssessorTests
{
    [Theory]
    [InlineData("^5.11.0", "5.11.0")]
    [InlineData("~4.7.0", "4.7.0")]
    [InlineData("v5.0.0", "5.0.0")]
    [InlineData(">=5.1", "5.1")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormaliseVersion_StripsPrefixes(string? input, string? expected)
    {
        Assert.Equal(expected, GitHubPrototypeCodebaseAssessor.NormaliseVersion(input));
    }

    [Theory]
    [InlineData("5.0.0", "5.11.0", true)]
    [InlineData("5.11.0", "5.11.0", false)]
    [InlineData("5.12.0", "5.11.0", false)]
    [InlineData("5.0", "5.0.0", false)]
    [InlineData(null, "5.11.0", null)]
    [InlineData("5.0.0", null, null)]
    public void IsBehind_ComparesSemanticVersions(string? current, string? latest, bool? expected)
    {
        Assert.Equal(expected, GitHubPrototypeCodebaseAssessor.IsBehind(current, latest));
    }

    [Fact]
    public void FindDependencyVersion_ReadsDependenciesAndDevDependencies()
    {
        using var document = JsonDocument.Parse("""
            {
              "dependencies": { "govuk-prototype-kit": "^13.0.0" },
              "devDependencies": { "govuk-frontend": "^5.4.0" }
            }
            """);

        Assert.Equal(
            "^13.0.0",
            GitHubPrototypeCodebaseAssessor.FindDependencyVersion(document.RootElement, "govuk-prototype-kit"));
        Assert.Equal(
            "^5.4.0",
            GitHubPrototypeCodebaseAssessor.FindDependencyVersion(document.RootElement, "govuk-frontend"));
        Assert.Null(
            GitHubPrototypeCodebaseAssessor.FindDependencyVersion(document.RootElement, "missing-package"));
    }

    [Fact]
    public void HasPrototypeKitScripts_DetectsKitCommands()
    {
        using var withKit = JsonDocument.Parse("""
            { "scripts": { "start": "govuk-prototype-kit serve" } }
            """);
        using var withoutKit = JsonDocument.Parse("""
            { "scripts": { "start": "node server.js" } }
            """);

        Assert.True(GitHubPrototypeCodebaseAssessor.HasPrototypeKitScripts(withKit.RootElement));
        Assert.False(GitHubPrototypeCodebaseAssessor.HasPrototypeKitScripts(withoutKit.RootElement));
    }

    [Fact]
    public async Task AssessAsync_ReturnsUnknown_WhenGitHubNotConfigured()
    {
        var assessor = CreateAssessor(new GitHubOptions());

        var result = await assessor.AssessAsync("https://github.com/DFE-Digital/example-prototype");

        Assert.Equal(PrototypeCodebaseSupport.Unknown, result.Support);
        Assert.Contains("GitHub access is not configured", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssessAsync_ReturnsUnknown_WhenRepositoryUrlInvalid()
    {
        var assessor = CreateAssessor(new GitHubOptions
        {
            PersonalAccessToken = "ghp_test_token_not_used"
        });

        var result = await assessor.AssessAsync("not-a-github-url");

        Assert.Equal(PrototypeCodebaseSupport.Unknown, result.Support);
        Assert.Contains("valid GitHub repository URL", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubPrototypeCodebaseAssessor CreateAssessor(GitHubOptions options)
    {
        var optionsMonitor = new StaticOptionsMonitor<GitHubOptions>(options);
        var httpClientFactory = new SingleClientHttpClientFactory(new HttpClient());
        var tokenProvider = new GitHubInstallationTokenProvider(
            httpClientFactory,
            optionsMonitor,
            NullLogger<GitHubInstallationTokenProvider>.Instance);
        var apiClient = new GitHubApiClient(httpClientFactory, optionsMonitor, tokenProvider);

        return new GitHubPrototypeCodebaseAssessor(
            apiClient,
            Options.Create(options),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GitHubPrototypeCodebaseAssessor>.Instance);
    }

    private sealed class SingleClientHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

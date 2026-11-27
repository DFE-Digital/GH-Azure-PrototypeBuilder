using System.Net.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class GitHubHealthReportBuilderTests
{
    [Fact]
    public void Build_ReportsHealthyWhenInstallationMatchesDfeDigitalAndPermissionsAreGranted()
    {
        var organisationId = Guid.NewGuid();
        var report = GitHubHealthReportBuilder.Build(ConfiguredAppSnapshot(
            installationJson: InstallationJson(suspended: false, contents: "write"),
            organisations:
            [
                new GitHubOrganisation
                {
                    Id = organisationId,
                    Name = "DFE-Digital",
                    CreatedBy = "system"
                }
            ],
            access: new Dictionary<string, GitHubOrganisationAccessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["DFE-Digital"] = new()
                {
                    CanAccess = true,
                    CanonicalLogin = "dfe-digital",
                    HtmlUrl = "https://github.com/dfe-digital"
                }
            }) with
        {
            AppJson = """
                {
                  "name": "DDT Prototype Manager",
                  "html_url": "https://github.com/apps/ddt-prototype-manager"
                }
                """,
            RateLimitJson = RateLimitJson(remaining: 4800),
            AccessibleRepositoryCount = 42,
            OrganisationAccessChecked = true
        });

        Assert.Equal(GitHubHealthStatus.Healthy, report.Status);
        Assert.Equal("DDT Prototype Manager", report.AppName);
        Assert.Equal("dfe-digital", report.Installation!.AccountLogin);
        Assert.Equal(6, report.SatisfiedPermissionCount);
        Assert.Contains(report.AdditionalPermissions, permission => permission.Label == "Members");
        var organisation = Assert.Single(report.Organisations);
        Assert.True(organisation.MatchesInstallation);
        Assert.True(organisation.CanAccess);
        Assert.Equal("dfe-digital", organisation.CanonicalLogin);
        Assert.Equal(42, report.AccessibleRepositoryCount);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Build_NeedsAttentionWhenARequiredPermissionIsOnlyRead()
    {
        var report = GitHubHealthReportBuilder.Build(ConfiguredAppSnapshot(
            installationJson: InstallationJson(suspended: false, contents: "read"),
            organisations: [Organisation("dfe-digital")],
            access: Accessible("dfe-digital")) with
        {
            OrganisationAccessChecked = true,
            RateLimitJson = RateLimitJson(remaining: 1000)
        });

        Assert.Equal(GitHubHealthStatus.Attention, report.Status);
        var contents = Assert.Single(report.Permissions, permission => permission.Label == "Contents");
        Assert.False(contents.IsSatisfied);
        Assert.Equal("Read", contents.GrantedLabel);
        Assert.Contains(report.Issues, issue => issue.Contains("Contents", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_IsUnavailableWhenTheInstallationIsSuspended()
    {
        var report = GitHubHealthReportBuilder.Build(ConfiguredAppSnapshot(
            installationJson: InstallationJson(suspended: true, contents: "write"),
            organisations: [Organisation("dfe-digital")],
            access: Accessible("dfe-digital")) with
        {
            OrganisationAccessChecked = true
        });

        Assert.Equal(GitHubHealthStatus.Unavailable, report.Status);
        Assert.True(report.Installation!.IsSuspended);
        Assert.Contains(report.Issues, issue => issue.Contains("suspended", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_IsUnavailableWhenGitHubIsNotConfigured()
    {
        var report = GitHubHealthReportBuilder.Build(new GitHubHealthSnapshot());

        Assert.Equal(GitHubHealthStatus.Unavailable, report.Status);
        Assert.Equal("Not configured", report.AuthenticationMode);
        Assert.Contains(report.Issues, issue => issue.Contains("GitHub App", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_NeedsAttentionWhenTheAllowedOrganisationIsNotAccessible()
    {
        var report = GitHubHealthReportBuilder.Build(ConfiguredAppSnapshot(
            installationJson: InstallationJson(suspended: false, contents: "write"),
            organisations: [Organisation("dfe-digital")],
            access: new Dictionary<string, GitHubOrganisationAccessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["dfe-digital"] = new()
                {
                    CanAccess = false,
                    Error = "Could not read the organisation (404)."
                }
            }) with
        {
            OrganisationAccessChecked = true,
            RateLimitJson = RateLimitJson(remaining: 1000)
        });

        Assert.Equal(GitHubHealthStatus.Attention, report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("dfe-digital", StringComparison.Ordinal));
        Assert.False(Assert.Single(report.Organisations).CanAccess);
    }

    [Fact]
    public void Build_IsUnavailableWhenTheRateLimitIsExhausted()
    {
        var report = GitHubHealthReportBuilder.Build(ConfiguredAppSnapshot(
            installationJson: InstallationJson(suspended: false, contents: "write"),
            organisations: [Organisation("dfe-digital")],
            access: Accessible("dfe-digital")) with
        {
            OrganisationAccessChecked = true,
            RateLimitJson = RateLimitJson(remaining: 0)
        });

        Assert.Equal(GitHubHealthStatus.Unavailable, report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("exhausted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetReportAsync_DoesNotCallGitHubWhenCredentialsAreMissing()
    {
        await using var context = await CreateContextAsync();
        var monitor = new StaticOptionsMonitor(new GitHubOptions());
        var factory = new ThrowingHttpClientFactory();
        var tokenProvider = new GitHubInstallationTokenProvider(
            factory,
            monitor,
            NullLogger<GitHubInstallationTokenProvider>.Instance);
        var client = new GitHubApiClient(factory, monitor, tokenProvider);
        var service = new GitHubHealthService(
            context,
            monitor,
            factory,
            client,
            NullLogger<GitHubHealthService>.Instance);

        var report = await service.GetReportAsync();

        Assert.Equal(GitHubHealthStatus.Unavailable, report.Status);
        Assert.False(report.IsConfigured);
    }

    private static GitHubHealthSnapshot ConfiguredAppSnapshot(
        string installationJson,
        IReadOnlyList<GitHubOrganisation> organisations,
        IReadOnlyDictionary<string, GitHubOrganisationAccessResult> access) => new()
    {
        IsConfigured = true,
        UsesGitHubApp = true,
        ValidateWithApi = true,
        AppId = 5934990,
        InstallationId = 186088276,
        PrivateKeyConfigured = true,
        ApiBaseUrl = "https://api.github.com",
        DefaultCollaboratorPermission = "admin",
        WorkflowBranch = "main",
        InstallationJson = installationJson,
        Organisations = organisations,
        OrganisationAccess = access
    };

    private static GitHubOrganisation Organisation(string name) => new()
    {
        Name = name,
        CreatedBy = "system"
    };

    private static Dictionary<string, GitHubOrganisationAccessResult> Accessible(string name) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [name] = new GitHubOrganisationAccessResult
            {
                CanAccess = true,
                CanonicalLogin = name,
                HtmlUrl = $"https://github.com/{name}"
            }
        };

    private static string InstallationJson(bool suspended, string contents) => $$"""
        {
          "account": {
            "login": "dfe-digital",
            "type": "Organization",
            "html_url": "https://github.com/dfe-digital"
          },
          "repository_selection": "all",
          "html_url": "https://github.com/organizations/dfe-digital/settings/installations/186088276",
          "suspended_at": {{(suspended ? "\"2026-01-01T00:00:00Z\"" : "null")}},
          "permissions": {
            "administration": "write",
            "contents": "{{contents}}",
            "workflows": "write",
            "actions": "write",
            "secrets": "write",
            "metadata": "read",
            "members": "read"
          }
        }
        """;

    private static string RateLimitJson(int remaining) => $$"""
        {
          "resources": {
            "core": {
              "limit": 5000,
              "remaining": {{remaining}},
              "reset": 1764270000,
              "used": {{5000 - remaining}}
            }
          }
        }
        """;

    private static async Task<PrototypeManagerDbContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PrototypeManagerDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new PrototypeManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed class StaticOptionsMonitor(GitHubOptions value) : IOptionsMonitor<GitHubOptions>
    {
        public GitHubOptions CurrentValue => value;
        public GitHubOptions Get(string? name) => value;
        public IDisposable OnChange(Action<GitHubOptions, string?> listener) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("GitHub should not be called when it is not configured.");
    }
}

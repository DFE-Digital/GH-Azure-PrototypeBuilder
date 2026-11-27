using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class GitHubOrganisationServiceTests
{
    [Fact]
    public async Task ValidateRepositoryUrlAsync_AllowsWhitelistedOwner()
    {
        await using var context = await CreateContextAsync();
        context.GitHubOrganisations.Add(new GitHubOrganisation
        {
            Name = "DFE-Digital",
            CreatedBy = "admin@education.gov.uk"
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        await service.ValidateRepositoryUrlAsync("https://github.com/DFE-Digital/compass");
    }

    [Fact]
    public async Task ValidateRepositoryUrlAsync_RejectsUnlistedOwner()
    {
        await using var context = await CreateContextAsync();
        context.GitHubOrganisations.Add(new GitHubOrganisation
        {
            Name = "DFE-Digital",
            CreatedBy = "admin@education.gov.uk"
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ValidateRepositoryUrlAsync("https://github.com/andyjones81/private-repo"));
    }

    [Fact]
    public async Task AddAsync_AcceptsOrganisationUrlAndRejectsDuplicates()
    {
        await using var context = await CreateContextAsync();
        var service = CreateService(context);

        await service.AddAsync("https://github.com/dfe-digital", "admin@education.gov.uk");

        var added = Assert.Single(context.GitHubOrganisations);
        Assert.Equal("dfe-digital", added.Name);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddAsync("DFE-Digital", "admin@education.gov.uk"));
        Assert.Contains("already an allowed", exception.Message);
    }

    [Fact]
    public async Task RemoveAsync_KeepsTheLastOrganisation()
    {
        await using var context = await CreateContextAsync();
        var organisation = new GitHubOrganisation
        {
            Name = "dfe-digital",
            CreatedBy = "admin@education.gov.uk"
        };
        context.GitHubOrganisations.Add(organisation);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RemoveAsync(organisation.Id, "admin@education.gov.uk"));

        Assert.Contains("At least one", exception.Message);
        Assert.Single(context.GitHubOrganisations);
    }

    [Fact]
    public async Task RemoveAsync_RemovesWhenAnotherOrganisationRemains()
    {
        await using var context = await CreateContextAsync();
        var first = new GitHubOrganisation { Name = "dfe-digital", CreatedBy = "admin@education.gov.uk" };
        var second = new GitHubOrganisation { Name = "example-org", CreatedBy = "admin@education.gov.uk" };
        context.GitHubOrganisations.AddRange(first, second);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        await service.RemoveAsync(second.Id, "admin@education.gov.uk");

        var remaining = Assert.Single(context.GitHubOrganisations);
        Assert.Equal("dfe-digital", remaining.Name);
        Assert.Contains(context.AuditEvents, audit => audit.Action == "GitHub organisation removed");
    }

    private static GitHubOrganisationService CreateService(PrototypeManagerDbContext context) =>
        new(context, new AuditService(context));

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
}

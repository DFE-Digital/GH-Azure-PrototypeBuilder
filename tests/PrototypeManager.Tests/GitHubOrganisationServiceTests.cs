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
    public async Task AddAsync_ParsesOrganisationUrl()
    {
        await using var context = await CreateContextAsync();
        var service = CreateService(context);

        await service.AddAsync("https://github.com/andyjones81", "admin@education.gov.uk");

        var organisation = await context.GitHubOrganisations.SingleAsync();
        Assert.Equal("andyjones81", organisation.Name);
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

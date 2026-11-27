using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubOrganisationService(
    PrototypeManagerDbContext dbContext,
    IAuditService auditService) : IGitHubOrganisationService
{
    public async Task<IReadOnlyList<GitHubOrganisation>> GetAllAsync()
    {
        var organisations = await dbContext.GitHubOrganisations
            .AsNoTracking()
            .ToListAsync();

        return organisations
            .OrderBy(organisation => organisation.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task ValidateRepositoryUrlAsync(string repositoryUrl, CancellationToken cancellationToken = default)
    {
        var normalizedUrl = PrototypeGitHubRepositoryUrls.Normalize(repositoryUrl);

        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            throw new ValidationException("Enter a GitHub repository URL.");
        }

        var owner = PrototypeGitHubRepositoryUrls.TryGetOwner(normalizedUrl);

        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ValidationException("Enter a valid GitHub repository URL.");
        }

        if (string.IsNullOrWhiteSpace(PrototypeGitHubRepositoryUrls.TryGetRepositoryName(normalizedUrl)))
        {
            throw new ValidationException("Enter a valid GitHub repository URL.");
        }

        var allowedOrganisations = await dbContext.GitHubOrganisations
            .AsNoTracking()
            .Select(organisation => organisation.Name)
            .ToListAsync(cancellationToken);

        if (allowedOrganisations.Count == 0)
        {
            throw new ValidationException("No GitHub organisations are configured. Contact an administrator.");
        }

        if (!allowedOrganisations.Any(allowedOrganisation =>
                string.Equals(allowedOrganisation, owner, StringComparison.OrdinalIgnoreCase)))
        {
            var allowedList = string.Join(", ", allowedOrganisations.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            throw new ValidationException(
                $"The repository must belong to one of these GitHub organisations or accounts: {allowedList}.");
        }
    }

    public async Task ValidateOwnerAsync(string owner, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ValidationException("Select a GitHub organisation.");
        }

        var allowedOrganisations = await dbContext.GitHubOrganisations
            .AsNoTracking()
            .Select(organisation => organisation.Name)
            .ToListAsync(cancellationToken);

        if (allowedOrganisations.Count == 0)
        {
            throw new ValidationException("No GitHub organisations are configured. Contact an administrator.");
        }

        if (!allowedOrganisations.Any(allowedOrganisation =>
                string.Equals(allowedOrganisation, owner.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            var allowedList = string.Join(", ", allowedOrganisations.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            throw new ValidationException(
                $"The repository must belong to one of these GitHub organisations or accounts: {allowedList}.");
        }
    }

    public async Task AddAsync(string nameOrUrl, string actorEmail)
    {
        var name = GitHubOrganisationNames.ParseInput(nameOrUrl);
        var existingNames = await dbContext.GitHubOrganisations
            .AsNoTracking()
            .Select(organisation => organisation.Name)
            .ToListAsync();

        if (existingNames.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException($"{name} is already an allowed GitHub organisation.");
        }

        dbContext.GitHubOrganisations.Add(new GitHubOrganisation
        {
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = string.IsNullOrWhiteSpace(actorEmail) ? "unknown" : actorEmail.Trim()
        });
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            null,
            actorEmail,
            "GitHub organisation added",
            $"Allowed GitHub organisation {name}.");
    }

    public async Task RemoveAsync(Guid id, string actorEmail)
    {
        var organisation = await dbContext.GitHubOrganisations
            .SingleOrDefaultAsync(item => item.Id == id);

        if (organisation is null)
        {
            throw new ValidationException("That GitHub organisation is not in the allowed list.");
        }

        if (await dbContext.GitHubOrganisations.CountAsync() <= 1)
        {
            throw new ValidationException(
                "At least one GitHub organisation must stay allowed. Add another organisation before removing this one.");
        }

        var name = organisation.Name;
        dbContext.GitHubOrganisations.Remove(organisation);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            null,
            actorEmail,
            "GitHub organisation removed",
            $"Removed allowed GitHub organisation {name}.");
    }
}

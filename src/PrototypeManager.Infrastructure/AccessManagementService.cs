using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class AccessManagementService(
    PrototypeManagerDbContext dbContext,
    IAuditService auditService) : IAccessManagementService
{
    public async Task<IReadOnlyList<BusinessArea>> GetVisibleBusinessAreasAsync(string email, bool isAdmin)
    {
        if (isAdmin)
        {
            return await dbContext.BusinessAreas.AsNoTracking()
                .OrderBy(x => x.Name)
                .ToListAsync();
        }

        var normalizedEmail = NormalizeEmail(email);
        var assignedIds = await AccessQueryHelpers.GetAssignedBusinessAreaIdsAsync(dbContext, normalizedEmail);

        return await dbContext.BusinessAreas.AsNoTracking()
            .Where(x => assignedIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<BusinessAreaSummary>> GetVisibleBusinessAreaSummariesAsync(string email, bool isAdmin)
    {
        var visibleAreas = await GetVisibleBusinessAreasAsync(email, isAdmin);
        var visibleAreaIds = visibleAreas.Select(x => x.Id).ToHashSet();
        var prototypes = (await dbContext.Prototypes.AsNoTracking().ToListAsync())
            .Where(prototype => prototype.Status != PrototypeStatus.Deleted);

        return visibleAreas
            .Select(area => new BusinessAreaSummary
            {
                Id = area.Id,
                Name = area.Name,
                PrototypeCount = prototypes.Count(prototype => prototype.BusinessAreaId == area.Id)
            })
            .Where(summary => isAdmin || visibleAreaIds.Contains(summary.Id))
            .OrderBy(summary => summary.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<AppUserAccessRecord>> GetUsersAsync()
    {
        var users = await dbContext.AppUsers.AsNoTracking()
            .Include(x => x.BusinessAreaAssignments)
            .ThenInclude(x => x.BusinessArea)
            .OrderBy(x => x.Email)
            .ToListAsync();

        return users.Select(user => new AppUserAccessRecord
        {
            Email = user.Email,
            DisplayName = user.DisplayName,
            IsAdmin = user.IsAdmin,
            BusinessAreaIds = user.BusinessAreaAssignments
                .Select(assignment => assignment.BusinessAreaId)
                .OrderBy(id => id)
                .ToList(),
            BusinessAreaNames = user.BusinessAreaAssignments
                .Where(assignment => assignment.BusinessArea is not null)
                .Select(assignment => assignment.BusinessArea!.Name)
                .OrderBy(name => name)
                .ToList()
        }).ToList();
    }

    public async Task SaveUserAccessAsync(UpdateUserAccessRequest request, string actorEmail)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ValidationException("Enter an email address for the user.");
        }

        var existingAreaIds = await dbContext.BusinessAreas.AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync();

        if (request.BusinessAreaIds.Except(existingAreaIds).Any())
        {
            throw new ValidationException("One or more selected business areas are not valid.");
        }

        var user = await dbContext.AppUsers
            .Include(x => x.BusinessAreaAssignments)
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail);

        if (user is null)
        {
            user = new AppUser
            {
                Email = normalizedEmail,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? normalizedEmail : request.DisplayName.Trim(),
                IsAdmin = request.IsAdmin,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.AppUsers.Add(user);
        }
        else
        {
            user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? user.DisplayName : request.DisplayName.Trim();
            user.IsAdmin = request.IsAdmin;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            dbContext.AppUserBusinessAreaAssignments.RemoveRange(user.BusinessAreaAssignments);
        }

        user.BusinessAreaAssignments = request.BusinessAreaIds
            .Distinct()
            .Select(businessAreaId => new AppUserBusinessAreaAssignment
            {
                AppUser = user,
                BusinessAreaId = businessAreaId,
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            null,
            actorEmail,
            "User access updated",
            $"Updated access for {normalizedEmail}. Admin: {request.IsAdmin}. Business areas: {request.BusinessAreaIds.Count}.");
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

internal static class AccessQueryHelpers
{
    public static async Task<List<Guid>> GetAssignedBusinessAreaIdsAsync(PrototypeManagerDbContext dbContext, string email) =>
        await dbContext.AppUsers.AsNoTracking()
            .Where(user => user.Email == email)
            .SelectMany(user => user.BusinessAreaAssignments.Select(assignment => assignment.BusinessAreaId))
            .ToListAsync();
}

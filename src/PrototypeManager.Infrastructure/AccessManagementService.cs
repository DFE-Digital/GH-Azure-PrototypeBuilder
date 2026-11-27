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

        var userAssignments = await dbContext.AppUserBusinessAreaAssignments.AsNoTracking()
            .Select(assignment => assignment.BusinessAreaId)
            .ToListAsync();

        return visibleAreas
            .Select(area => new BusinessAreaSummary
            {
                Id = area.Id,
                Name = area.Name,
                PrototypeCount = prototypes.Count(prototype => prototype.BusinessAreaId == area.Id),
                UserCount = userAssignments.Count(businessAreaId => businessAreaId == area.Id)
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

        var prototypeAccessCounts = await GetPrototypeAccessCountsAsync();

        return users.Select(user => new AppUserAccessRecord
        {
            Email = user.Email,
            DisplayName = user.DisplayName,
            EntraObjectId = user.EntraObjectId,
            LastSignedInAt = user.LastSignedInAt,
            IsAdmin = user.IsAdmin,
            PrototypeAccessCount = prototypeAccessCounts.GetValueOrDefault(user.Email),
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

    public async Task<IReadOnlyList<AppUserAccessRecord>> GetAdminsAsync()
    {
        var admins = await GetUsersAsync();
        return admins.Where(user => user.IsAdmin).ToList();
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
                // Name/profile filled from Microsoft Entra when they first sign in.
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? string.Empty : request.DisplayName.Trim(),
                IsAdmin = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.AppUsers.Add(user);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
            {
                user.DisplayName = request.DisplayName.Trim();
            }

            user.UpdatedAt = DateTimeOffset.UtcNow;
            dbContext.AppUserBusinessAreaAssignments.RemoveRange(user.BusinessAreaAssignments);
        }

        // Business-area access page does not change admin status.
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
            $"Updated business area access for {normalizedEmail}. Business areas: {request.BusinessAreaIds.Count}.");
    }

    public async Task SetPrototypeManagerAdminAsync(SetPrototypeManagerAdminRequest request, string actorEmail)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ValidationException("Enter an email address for the admin.");
        }

        var user = await dbContext.AppUsers
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail);

        if (user is null)
        {
            user = new AppUser
            {
                Email = normalizedEmail,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? normalizedEmail : request.DisplayName.Trim(),
                IsAdmin = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.AppUsers.Add(user);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
            {
                user.DisplayName = request.DisplayName.Trim();
            }

            user.IsAdmin = true;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            null,
            actorEmail,
            "Prototype Manager admin added",
            $"Granted Prototype Manager admin access to {normalizedEmail}.");
    }

    public async Task RemovePrototypeManagerAdminAsync(string email, string actorEmail)
    {
        var normalizedEmail = NormalizeEmail(email);
        var normalizedActor = NormalizeEmail(actorEmail);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ValidationException("Enter an email address for the admin.");
        }

        if (string.Equals(normalizedEmail, normalizedActor, StringComparison.Ordinal))
        {
            throw new ValidationException("You cannot remove your own Prototype Manager admin access.");
        }

        var user = await dbContext.AppUsers.SingleOrDefaultAsync(x => x.Email == normalizedEmail);

        if (user is null || !user.IsAdmin)
        {
            throw new ValidationException("That person is not a Prototype Manager admin.");
        }

        var remainingAdmins = await dbContext.AppUsers.CountAsync(x => x.IsAdmin && x.Email != normalizedEmail);
        if (remainingAdmins == 0)
        {
            throw new ValidationException("At least one Prototype Manager admin must remain.");
        }

        user.IsAdmin = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            null,
            actorEmail,
            "Prototype Manager admin removed",
            $"Removed Prototype Manager admin access from {normalizedEmail}.");
    }

    public async Task<UserAccessRemovalPreview> GetUserAccessRemovalPreviewAsync(string email)
    {
        var normalizedEmail = NormalizeEmail(email);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ValidationException("Enter an email address.");
        }

        var user = await dbContext.AppUsers.AsNoTracking()
            .Include(x => x.BusinessAreaAssignments)
            .ThenInclude(x => x.BusinessArea)
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail);

        var prototypes = (await dbContext.Prototypes
            .Include(x => x.Contacts)
            .Where(x => x.Status != PrototypeStatus.Deleted)
            .ToListAsync())
            .Where(prototype =>
                string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
                prototype.Contacts.Any(contact =>
                    string.Equals(contact.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(prototype => prototype.Name)
            .ToList();

        var accessItems = new List<UserPrototypeAccessItem>();
        var blocked = new List<UserPrototypeAccessItem>();

        foreach (var prototype in prototypes)
        {
            if (string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                var successor = ResolveOwnershipSuccessor(prototype, normalizedEmail);
                if (successor is null)
                {
                    blocked.Add(new UserPrototypeAccessItem
                    {
                        PrototypeId = prototype.Id,
                        PrototypeName = prototype.Name,
                        Role = "Owner",
                        PlannedAction = "Blocked — assign a deputy or manager first, then try again."
                    });
                }
                else
                {
                    accessItems.Add(new UserPrototypeAccessItem
                    {
                        PrototypeId = prototype.Id,
                        PrototypeName = prototype.Name,
                        Role = "Owner",
                        PlannedAction = $"Transfer ownership to {successor}."
                    });
                }

                continue;
            }

            if (string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                accessItems.Add(new UserPrototypeAccessItem
                {
                    PrototypeId = prototype.Id,
                    PrototypeName = prototype.Name,
                    Role = "Deputy owner",
                    PlannedAction = "Remove as deputy owner."
                });
                continue;
            }

            accessItems.Add(new UserPrototypeAccessItem
            {
                PrototypeId = prototype.Id,
                PrototypeName = prototype.Name,
                Role = "Manager",
                PlannedAction = "Remove as prototype manager."
            });
        }

        return new UserAccessRemovalPreview
        {
            Email = normalizedEmail,
            DisplayName = user?.DisplayName ?? normalizedEmail,
            IsAdmin = user?.IsAdmin ?? false,
            HasAppUserRecord = user is not null,
            BusinessAreaNames = user?.BusinessAreaAssignments
                .Where(assignment => assignment.BusinessArea is not null)
                .Select(assignment => assignment.BusinessArea!.Name)
                .OrderBy(name => name)
                .ToList() ?? [],
            PrototypeAccess = accessItems,
            BlockedOwnership = blocked,
            CanRevoke = blocked.Count == 0
        };
    }

    public async Task RevokeUserAccessAsync(string email, string actorEmail)
    {
        var normalizedEmail = NormalizeEmail(email);
        var normalizedActor = NormalizeEmail(actorEmail);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ValidationException("Enter an email address.");
        }

        if (string.Equals(normalizedEmail, normalizedActor, StringComparison.Ordinal))
        {
            throw new ValidationException("You cannot revoke your own access.");
        }

        var preview = await GetUserAccessRemovalPreviewAsync(normalizedEmail);
        if (!preview.CanRevoke)
        {
            throw new ValidationException(
                "This person still owns one or more prototypes with no deputy or manager to take over. Assign a successor on those prototypes first.");
        }

        var user = await dbContext.AppUsers
            .Include(x => x.BusinessAreaAssignments)
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail);

        if (user?.IsAdmin == true)
        {
            var remainingAdmins = await dbContext.AppUsers.CountAsync(x => x.IsAdmin && x.Email != normalizedEmail);
            if (remainingAdmins == 0)
            {
                throw new ValidationException("At least one Prototype Manager admin must remain. Remove admin access from another person first, or keep this user as admin.");
            }
        }

        var prototypes = await dbContext.Prototypes
            .Include(x => x.Contacts)
            .Where(x => x.Status != PrototypeStatus.Deleted)
            .ToListAsync();

        var affected = prototypes
            .Where(prototype =>
                string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
                prototype.Contacts.Any(contact =>
                    string.Equals(contact.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var prototype in affected)
        {
            if (string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                var successor = ResolveOwnershipSuccessor(prototype, normalizedEmail)
                    ?? throw new ValidationException($"No successor found for prototype '{prototype.Name}'.");

                TransferOwnershipInMemory(prototype, successor);
            }

            if (string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                prototype.DeputyOwnerEmail = string.Empty;
            }

            var contactsToRemove = prototype.Contacts
                .Where(contact => string.Equals(contact.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (contactsToRemove.Count > 0)
            {
                dbContext.PrototypeContacts.RemoveRange(contactsToRemove);
            }
        }

        if (user is not null)
        {
            dbContext.AppUserBusinessAreaAssignments.RemoveRange(user.BusinessAreaAssignments);
            dbContext.AppUsers.Remove(user);
        }

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            null,
            actorEmail,
            "User access revoked",
            $"Revoked Prototype Manager and prototype access for {normalizedEmail}. Prototypes updated: {affected.Count}.");
    }

    private async Task<Dictionary<string, int>> GetPrototypeAccessCountsAsync()
    {
        var prototypes = await dbContext.Prototypes.AsNoTracking()
            .Include(x => x.Contacts)
            .Where(x => x.Status != PrototypeStatus.Deleted)
            .ToListAsync();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Increment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var key = NormalizeEmail(value);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        foreach (var prototype in prototypes)
        {
            Increment(prototype.OwnerEmail);
            Increment(prototype.DeputyOwnerEmail);
            foreach (var contact in prototype.Contacts)
            {
                Increment(contact.Email);
            }
        }

        return counts;
    }

    private static string? ResolveOwnershipSuccessor(Prototype prototype, string leavingEmail)
    {
        if (!string.IsNullOrWhiteSpace(prototype.DeputyOwnerEmail) &&
            !string.Equals(prototype.DeputyOwnerEmail, leavingEmail, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeEmail(prototype.DeputyOwnerEmail);
        }

        return prototype.Contacts
            .Where(contact => !string.Equals(contact.Email, leavingEmail, StringComparison.OrdinalIgnoreCase))
            .Select(contact => contact.Email)
            .OrderBy(email => email, StringComparer.OrdinalIgnoreCase)
            .Select(NormalizeEmail)
            .FirstOrDefault();
    }

    private void TransferOwnershipInMemory(Prototype prototype, string successorEmail)
    {
        var wasDeputy = string.Equals(prototype.DeputyOwnerEmail, successorEmail, StringComparison.OrdinalIgnoreCase);
        var successorContact = prototype.Contacts
            .SingleOrDefault(contact => string.Equals(contact.Email, successorEmail, StringComparison.OrdinalIgnoreCase));

        if (!wasDeputy && successorContact is null)
        {
            throw new ValidationException($"No successor found for prototype '{prototype.Name}'.");
        }

        prototype.OwnerEmail = successorEmail;

        if (wasDeputy)
        {
            prototype.DeputyOwnerEmail = string.Empty;
        }

        if (successorContact is not null)
        {
            dbContext.PrototypeContacts.Remove(successorContact);
            prototype.Contacts.Remove(successorContact);
        }
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

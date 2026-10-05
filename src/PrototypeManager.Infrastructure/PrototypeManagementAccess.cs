using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal static class PrototypeManagementAccess
{
    public static bool CanManagePrototype(
        Prototype prototype,
        IReadOnlyList<PrototypeContact> contacts,
        string email,
        bool isAdmin,
        bool isSupport)
    {
        if (isAdmin || isSupport)
        {
            return true;
        }

        var normalizedEmail = NormalizeEmail(email);

        return string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
               contacts.Any(contact => string.Equals(contact.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

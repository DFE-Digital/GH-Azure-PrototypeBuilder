using PrototypeManager.Domain;

namespace PrototypeManager.Web;

public static class PrototypeAccess
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

    public static bool CanManageContacts(Prototype prototype, string email, bool isAdmin, bool isSupport) =>
        isAdmin ||
        isSupport ||
        string.Equals(prototype.OwnerEmail, NormalizeEmail(email), StringComparison.OrdinalIgnoreCase);

    public static bool CanDeletePrototype(Prototype prototype, string email, bool isAdmin, bool isSupport)
    {
        if (isAdmin || isSupport)
        {
            return true;
        }

        return string.Equals(prototype.OwnerEmail, NormalizeEmail(email), StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanViewPrototypePassword(
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
               string.Equals(prototype.CreatedBy, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
               contacts.Any(contact => string.Equals(contact.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

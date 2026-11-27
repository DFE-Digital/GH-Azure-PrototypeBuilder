using Microsoft.AspNetCore.Mvc;

namespace PrototypeManager.Web.Pages.Admin;

public static class AdminNavigation
{
    public const string DashboardSection = "dashboard";
    public const string UsersSection = "users";
    public const string AdminsSection = "admins";
    public const string RemoveAccessSection = "remove-access";
    public const string PrototypesSection = "prototypes";
    public const string GitHubCleanupSection = "github-cleanup";
    public const string ExceptionsSection = "exceptions";

    public const string SectionViewDataKey = "AdminSection";

    public static string SectionUrl(IUrlHelper urlHelper, string section) =>
        section switch
        {
            UsersSection => urlHelper.Page("/Admin/Users") ?? string.Empty,
            AdminsSection => urlHelper.Page("/Admin/Admins") ?? string.Empty,
            RemoveAccessSection => urlHelper.Page("/Admin/RemoveAccess") ?? string.Empty,
            PrototypesSection => urlHelper.Page("/Admin/Prototypes") ?? string.Empty,
            GitHubCleanupSection => urlHelper.Page("/Admin/GitHubCleanup") ?? string.Empty,
            ExceptionsSection => urlHelper.Page("/Admin/Exceptions") ?? string.Empty,
            _ => urlHelper.Page("/Admin/Index") ?? string.Empty
        };

    public static string SectionTitle(string section) =>
        section switch
        {
            UsersSection => "Business area access",
            AdminsSection => "Prototype Manager admins",
            RemoveAccessSection => "Remove access",
            PrototypesSection => "Manage prototypes",
            GitHubCleanupSection => "GitHub cleanup",
            ExceptionsSection => "Exception requests",
            _ => "Dashboard"
        };
}

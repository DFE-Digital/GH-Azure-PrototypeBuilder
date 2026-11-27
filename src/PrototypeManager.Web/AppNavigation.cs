using Microsoft.AspNetCore.Mvc;

namespace PrototypeManager.Web;

public sealed record AppNavItem(
    string Title,
    string Page,
    string? MatchPrefix = null,
    bool AdminOnly = false,
    bool IsChild = false);

public static class AppNavigation
{
    public static IReadOnlyList<AppNavItem> PrimaryItems { get; } =
    [
        new("Dashboard", "/Index", "/"),
        new("Prototypes", "/Prototypes/List", "/Prototypes"),
        new("Create a prototype", "/Prototypes/Create", "/Prototypes/Create"),
        new("Host an existing prototype", "/Prototypes/New", "/Prototypes/New"),
        new("Features", "/Features", "/Features"),
        new("Getting started", "/GettingStarted", "/Guidance")
    ];

    public static IReadOnlyList<AppNavItem> AdminItems { get; } =
    [
        new("Admin overview", "/Admin/Index", "/Admin", AdminOnly: true),
        new("Business area access", "/Admin/Users", "/Admin/Users", AdminOnly: true, IsChild: true),
        new("Prototype Manager admins", "/Admin/Admins", "/Admin/Admins", AdminOnly: true, IsChild: true),
        new("Remove access", "/Admin/RemoveAccess", "/Admin/RemoveAccess", AdminOnly: true, IsChild: true),
        new("Manage prototypes", "/Admin/Prototypes", "/Admin/Prototype", AdminOnly: true, IsChild: true),
        new("Dependency health", "/Admin/DependencyHealth", "/Admin/Dependency", AdminOnly: true, IsChild: true),
        new("GitHub cleanup", "/Admin/GitHubCleanup", "/Admin/GitHubCleanup", AdminOnly: true, IsChild: true),
        new("Exception requests", "/Admin/Exceptions", "/Admin/Exceptions", AdminOnly: true, IsChild: true)
    ];

    public static string? Href(IUrlHelper urlHelper, AppNavItem item) =>
        urlHelper.Page(item.Page);

    public static bool IsActive(string? requestPath, AppNavItem item)
    {
        var path = string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath.TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        if (item.Page is "/Index")
        {
            return path is "/" or "/Index";
        }

        var match = (item.MatchPrefix ?? item.Page).TrimEnd('/');
        if (path.Equals(match, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Prototypes list matches /prototypes (pretty route), /Prototypes/List, and detail/setup routes,
        // but not Create/New
        if (item.Page is "/Prototypes/List")
        {
            if (path.StartsWith("/Prototypes/Create", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Prototypes/New", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return path.Equals("/prototypes", StringComparison.OrdinalIgnoreCase) ||
                   path.Equals("/Prototypes/List", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/prototypes/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Prototypes/", StringComparison.OrdinalIgnoreCase);
        }

        if (item.Page is "/Admin/Index")
        {
            return path.Equals("/Admin", StringComparison.OrdinalIgnoreCase) ||
                   path.Equals("/Admin/Index", StringComparison.OrdinalIgnoreCase);
        }

        if (item.Page is "/Features")
        {
            return path.Equals("/Features", StringComparison.OrdinalIgnoreCase);
        }

        if (item.Page is "/GettingStarted")
        {
            return path.Equals("/GettingStarted", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/Guidance", StringComparison.OrdinalIgnoreCase);
        }

        return path.StartsWith(match, StringComparison.OrdinalIgnoreCase);
    }
}

using Microsoft.AspNetCore.Mvc;

namespace PrototypeManager.Web.Pages.Admin;

public static class AdminNavigation
{
    public const string DashboardSection = "dashboard";
    public const string UsersSection = "users";
    public const string GitHubOrganisationsSection = "github-organisations";
    public const string ExceptionsSection = "exceptions";

    public const string SectionViewDataKey = "AdminSection";

    public static string SectionUrl(Microsoft.AspNetCore.Mvc.IUrlHelper urlHelper, string section) =>
        section switch
        {
            UsersSection => urlHelper.Page("/Admin/Users") ?? string.Empty,
            GitHubOrganisationsSection => urlHelper.Page("/Admin/GitHubOrganisations") ?? string.Empty,
            ExceptionsSection => urlHelper.Page("/Admin/Exceptions") ?? string.Empty,
            _ => urlHelper.Page("/Admin/Index") ?? string.Empty
        };

    public static string SectionTitle(string section) =>
        section switch
        {
            UsersSection => "Manage access",
            GitHubOrganisationsSection => "GitHub organisations",
            ExceptionsSection => "Exception requests",
            _ => "Dashboard"
        };
}

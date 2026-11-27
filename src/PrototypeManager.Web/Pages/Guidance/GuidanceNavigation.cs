using Microsoft.AspNetCore.Mvc;

namespace PrototypeManager.Web.Pages.Guidance;

public sealed record GuidanceNavItem(string Id, string Title, string Page);

public static class GuidanceNavigation
{
    public const string SectionViewDataKey = "GuidanceSection";

    public const string Overview = "overview";
    public const string CreateOrHost = "create-or-host";
    public const string FindAndOpen = "find-and-open";
    public const string SetupAndDeploy = "setup-and-deploy";
    public const string Manage = "manage";
    public const string AdminAndLimits = "admin-and-limits";
    public const string Technical = "technical";
    public const string TechnicalAzure = "technical-azure";
    public const string TechnicalGitHub = "technical-github";
    public const string TechnicalOperations = "technical-operations";

    public static IReadOnlyList<GuidanceNavItem> UserGuideItems { get; } =
    [
        new(Overview, "Overview", "/GettingStarted"),
        new(CreateOrHost, "Create or host", "/Guidance/CreateOrHost"),
        new(FindAndOpen, "Find and open", "/Guidance/FindAndOpen"),
        new(SetupAndDeploy, "Setup and deploy", "/Guidance/SetupAndDeploy"),
        new(Manage, "Manage a prototype", "/Guidance/Manage"),
        new(AdminAndLimits, "Admin and limits", "/Guidance/AdminAndLimits")
    ];

    public static IReadOnlyList<GuidanceNavItem> TechnicalItems { get; } =
    [
        new(Technical, "How it works", "/Guidance/Technical"),
        new(TechnicalAzure, "Azure hosting", "/Guidance/Technical/Azure"),
        new(TechnicalGitHub, "GitHub integration", "/Guidance/Technical/GitHub"),
        new(TechnicalOperations, "Jobs and access", "/Guidance/Technical/Operations")
    ];

    public static string Href(IUrlHelper urlHelper, GuidanceNavItem item) =>
        urlHelper.Page(item.Page) ?? item.Page;

    public static string Title(string sectionId) =>
        UserGuideItems.Concat(TechnicalItems)
            .FirstOrDefault(item => item.Id == sectionId)
            ?.Title
        ?? "Guidance";
}

using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Web.Pages.Prototypes;

namespace PrototypeManager.Web.Pages;

public sealed class IndexModel(
    IPrototypeService prototypeService,
    IAccessManagementService accessManagementService) : PageModel
{
    public IReadOnlyList<Prototype> MyPrototypes { get; private set; } = [];
    public IReadOnlyList<BusinessAreaSummary> BusinessAreas { get; private set; } = [];
    public IReadOnlyList<Prototype> FailedDeployments { get; private set; } = [];

    public string UserEmail => User.GetEmail();

    public int LiveCount => MyPrototypes.Count(prototype => prototype.Status == PrototypeStatus.Live);

    public int ActiveJobCount => MyPrototypes.Count(prototype =>
        prototype.Status is PrototypeStatus.Requested
            or PrototypeStatus.Queued
            or PrototypeStatus.Provisioning
            or PrototypeStatus.Deleting);

    public int FailedCount => MyPrototypes.Count(prototype => prototype.Status == PrototypeStatus.Failed);

    public bool HasAttentionItems => FailedDeployments.Any();

    public IReadOnlyList<Prototype> RecentPrototypes => MyPrototypes.Take(8).ToList();

    public async Task OnGetAsync()
    {
        var isAdmin = User.IsAdmin();
        var isSupport = User.IsSupport();

        MyPrototypes = await prototypeService.GetVisibleAsync(UserEmail, isAdmin, isSupport);
        BusinessAreas = await accessManagementService.GetVisibleBusinessAreaSummariesAsync(UserEmail, isAdmin);
        FailedDeployments = await prototypeService.GetFailedAsync(UserEmail, isAdmin, isSupport);
    }

    public static string GetStatusBadgeClass(PrototypeStatus status) => ListModel.GetStatusBadgeClass(status);

    public static string GetStatusLabel(PrototypeStatus status) => ListModel.GetStatusLabel(status);
}

using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class IndexModel(
    IPrototypeService prototypeService,
    IAccessManagementService accessManagementService) : PageModel
{
    public IReadOnlyList<Prototype> AllPrototypes { get; private set; } = [];
    public IReadOnlyList<Prototype> FailedPrototypes { get; private set; } = [];
    public IReadOnlyList<Prototype> ExpiringSoon { get; private set; } = [];
    public IReadOnlyList<BusinessAreaSummary> BusinessAreas { get; private set; } = [];
    public IReadOnlyList<AppUserAccessRecord> Users { get; private set; } = [];

    public async Task OnGetAsync()
    {
        AllPrototypes = await prototypeService.GetAllAsync();
        FailedPrototypes = await prototypeService.GetFailedAsync();
        ExpiringSoon = await prototypeService.GetExpiringSoonAsync();
        BusinessAreas = await accessManagementService.GetVisibleBusinessAreaSummariesAsync(User.GetEmail(), true);
        Users = await accessManagementService.GetUsersAsync();
    }
}

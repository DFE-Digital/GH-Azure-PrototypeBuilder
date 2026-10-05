using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Web;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class DeleteModel(
    IPrototypeService prototypeService,
    IProvisioningProgressService provisioningProgressService) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (prototype is null)
        {
            return NotFound();
        }

        return await RedirectAfterDeleteActionAsync(id, prototype);
    }

    public async Task<IActionResult> OnPostAsync(Guid id, [FromForm] string? confirmName)
    {
        var prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (prototype is null)
        {
            return NotFound();
        }

        var contacts = await prototypeService.GetContactsAsync(id);

        if (!PrototypeAccess.CanDeletePrototype(prototype, User.GetEmail(), User.IsAdmin(), User.IsSupport()))
        {
            StatusMessage = "Only the prototype owner or an administrator can delete this prototype.";
            return await RedirectAfterDeleteActionAsync(id, prototype);
        }

        if (!string.Equals(confirmName?.Trim(), prototype.Name, StringComparison.Ordinal))
        {
            StatusMessage = "Deletion was not confirmed. Type the prototype app name exactly to delete it.";
            return await RedirectAfterDeleteActionAsync(id, prototype);
        }

        await prototypeService.DeleteAsync(id, User.GetEmail());
        StatusMessage = $"{prototype.Name} has been queued for deletion.";
        return RedirectToPage("/Prototypes/List");
    }

    private async Task<IActionResult> RedirectAfterDeleteActionAsync(Guid id, Prototype prototype)
    {
        var jobs = await prototypeService.GetJobsAsync(id);
        var auditEvents = await prototypeService.GetAuditEventsAsync(id);

        if (provisioningProgressService.ShouldShowProgress(prototype, jobs, auditEvents))
        {
            return RedirectToPage("/Prototypes/Setup", new { id });
        }

        return RedirectToPage("/Prototypes/Details", new { id, section = DetailsModel.SettingsSection });
    }
}

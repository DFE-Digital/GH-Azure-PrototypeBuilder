using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class ArchiveModel(IPrototypeService prototypeService) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    public IActionResult OnGet(Guid id) =>
        RedirectToPage("/Prototypes/Details", new { id, section = DetailsModel.SettingsSection });

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        await prototypeService.ArchiveAsync(id, User.GetEmail());
        StatusMessage = "Prototype archived.";
        return RedirectToPage("/Prototypes/Details", new { id, section = DetailsModel.SettingsSection });
    }
}

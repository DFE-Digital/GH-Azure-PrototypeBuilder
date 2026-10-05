using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class RetryScaffoldModel(IPrototypeService prototypeService) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    public IActionResult OnGet(Guid id) =>
        RedirectToPage("/Prototypes/Setup", new { id });

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        var prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (prototype is null)
        {
            return NotFound();
        }

        var contacts = await prototypeService.GetContactsAsync(id);

        if (!PrototypeAccess.CanManagePrototype(
                prototype,
                contacts,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport()))
        {
            StatusMessage = "You do not have permission to retry GitHub setup for this prototype.";
            return RedirectToPage("/Prototypes/Setup", new { id });
        }

        try
        {
            await prototypeService.RetryScaffoldGitHubRepositoryAsync(id, User.GetEmail());
            StatusMessage = "GitHub repository setup retry queued. This page will update automatically.";
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }

        return RedirectToPage("/Prototypes/Setup", new { id });
    }
}

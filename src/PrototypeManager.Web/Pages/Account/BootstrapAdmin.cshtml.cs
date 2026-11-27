using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages.Account;

public sealed class BootstrapAdminModel(
    IAccessManagementService accessManagementService) : PageModel
{
    public string Email => User.GetEmail();

    public string DisplayName => User.GetDisplayName();

    public bool AlreadyConfigured { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        AlreadyConfigured = await accessManagementService.HasPrototypeManagerAdminAsync();
        if (AlreadyConfigured && User.IsAdmin())
        {
            return RedirectToPage("/Admin/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        AlreadyConfigured = await accessManagementService.HasPrototypeManagerAdminAsync();
        if (AlreadyConfigured)
        {
            ModelState.AddModelError(
                string.Empty,
                "Prototype Manager already has an admin. Ask an existing admin to grant access.");
            return Page();
        }

        try
        {
            await accessManagementService.BootstrapFirstAdminAsync(Email, DisplayName);
            StatusMessage = "You are now a Prototype Manager admin.";
            return RedirectToPage("/Admin/Index");
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}

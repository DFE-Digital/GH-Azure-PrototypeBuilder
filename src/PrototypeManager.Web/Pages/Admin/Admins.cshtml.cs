using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class AdminsModel(
    IAccessManagementService accessManagementService) : PageModel
{
    public IReadOnlyList<AppUserAccessRecord> Admins { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await accessManagementService.SetPrototypeManagerAdminAsync(new SetPrototypeManagerAdminRequest
            {
                Email = Input.Email,
                DisplayName = Input.DisplayName
            }, User.GetEmail());

            StatusMessage = "Prototype Manager admin access granted.";
            return RedirectToPage();
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(string email)
    {
        try
        {
            await accessManagementService.RemovePrototypeManagerAdminAsync(email, User.GetEmail());
            StatusMessage = "Prototype Manager admin access removed.";
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Admins = await accessManagementService.GetAdminsAsync();
    }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Display name")]
        public string DisplayName { get; set; } = string.Empty;
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class RemoveAccessModel(
    IAccessManagementService accessManagementService) : PageModel
{
    public UserAccessRemovalPreview? Preview { get; private set; }

    [BindProperty]
    public LookupModel Lookup { get; set; } = new();

    [BindProperty]
    public string ConfirmEmail { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        Lookup.Email = email.Trim();
        await LoadPreviewAsync();
    }

    public async Task<IActionResult> OnPostLookupAsync()
    {
        if (!TryValidateModel(Lookup, nameof(Lookup)))
        {
            return Page();
        }

        return RedirectToPage(new { email = Lookup.Email.Trim() });
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(ConfirmEmail))
        {
            ModelState.AddModelError(nameof(ConfirmEmail), "Confirm the email address to remove access.");
            Lookup.Email = ConfirmEmail;
            return Page();
        }

        Lookup.Email = ConfirmEmail.Trim();

        try
        {
            await LoadPreviewAsync();

            if (Preview is null)
            {
                ModelState.AddModelError(string.Empty, "Enter an email address.");
                return Page();
            }

            if (!Preview.CanRevoke)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "This person still owns prototypes with no deputy or manager to take over. Assign a successor first.");
                return Page();
            }

            await accessManagementService.RevokeUserAccessAsync(ConfirmEmail, User.GetEmail());
            StatusMessage = $"Access removed for {ConfirmEmail.Trim().ToLowerInvariant()}.";
            return RedirectToPage("/Admin/Users");
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadPreviewAsync();
            return Page();
        }
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            Preview = await accessManagementService.GetUserAccessRemovalPreviewAsync(Lookup.Email);
            ConfirmEmail = Preview.Email;
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            Preview = null;
        }
    }

    public sealed class LookupModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }
}

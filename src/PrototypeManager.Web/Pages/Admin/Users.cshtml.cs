using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class UsersModel(
    IAccessManagementService accessManagementService) : PageModel
{
    public IReadOnlyList<BusinessArea> BusinessAreas { get; private set; } = [];
    public IReadOnlyList<AppUserAccessRecord> Users { get; private set; } = [];

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
            await accessManagementService.SaveUserAccessAsync(new UpdateUserAccessRequest
            {
                Email = Input.Email,
                DisplayName = string.Empty,
                BusinessAreaIds = Input.BusinessAreaIds
            }, User.GetEmail());

            StatusMessage = "Access saved. Their name and Microsoft profile will update when they sign in.";
            return RedirectToPage();
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        BusinessAreas = await accessManagementService.GetVisibleBusinessAreasAsync(User.GetEmail(), true);
        Users = await accessManagementService.GetUsersAsync();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter their work email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        public List<Guid> BusinessAreaIds { get; set; } = [];
    }
}

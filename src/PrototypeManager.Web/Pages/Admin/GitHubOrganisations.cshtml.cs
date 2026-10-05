using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class GitHubOrganisationsModel(
    IGitHubOrganisationService githubOrganisationService) : PageModel
{
    public IReadOnlyList<GitHubOrganisation> Organisations { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        await LoadAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await githubOrganisationService.AddAsync(Input.Organisation, User.GetEmail());
            StatusMessage = "GitHub organisation added.";
            return RedirectToPage();
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(nameof(Input.Organisation), exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id)
    {
        await githubOrganisationService.RemoveAsync(id, User.GetEmail());
        StatusMessage = "GitHub organisation removed.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Organisations = await githubOrganisationService.GetAllAsync();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter a GitHub organisation or account URL.")]
        [Display(Name = "GitHub organisation or account")]
        public string Organisation { get; set; } = string.Empty;
    }
}

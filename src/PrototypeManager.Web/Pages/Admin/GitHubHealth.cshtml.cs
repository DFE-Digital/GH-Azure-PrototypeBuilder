using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class GitHubHealthModel(
    IGitHubHealthService gitHubHealthService,
    IGitHubOrganisationService gitHubOrganisationService) : PageModel
{
    public GitHubHealthReport Report { get; private set; } = new();

    [BindProperty]
    [Display(Name = "GitHub organisation or account")]
    public string OrganisationName { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Report = await gitHubHealthService.GetReportAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(OrganisationName))
        {
            ModelState.AddModelError(nameof(OrganisationName), "Enter a GitHub organisation or account.");
        }

        if (!ModelState.IsValid)
        {
            Report = await gitHubHealthService.GetReportAsync(cancellationToken);
            return Page();
        }

        try
        {
            await gitHubOrganisationService.AddAsync(OrganisationName, User.GetEmail());
            StatusMessage = "GitHub organisation added.";
            return RedirectToPage();
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(nameof(OrganisationName), exception.Message);
            Report = await gitHubHealthService.GetReportAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id)
    {
        try
        {
            await gitHubOrganisationService.RemoveAsync(id, User.GetEmail());
            StatusMessage = "GitHub organisation removed.";
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }

        return RedirectToPage();
    }
}

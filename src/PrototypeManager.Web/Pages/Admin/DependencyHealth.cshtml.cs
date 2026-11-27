using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class DependencyHealthModel(
    IGitHubHealthService gitHubHealthService,
    IAzureHealthService azureHealthService,
    IGitHubOrganisationService gitHubOrganisationService) : PageModel
{
    public GitHubHealthReport GitHub { get; private set; } = new();
    public AzureHealthReport Azure { get; private set; } = new();

    public DependencyHealthStatus OverallStatus => DependencyHealth.Combine(GitHub.Status, Azure.Status);

    public string OverallLabel => DependencyHealth.Label(OverallStatus);

    public DateTimeOffset CheckedAt => GitHub.CheckedAt > Azure.CheckedAt ? GitHub.CheckedAt : Azure.CheckedAt;

    [BindProperty]
    [Display(Name = "GitHub organisation or account")]
    public string OrganisationName { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(OrganisationName))
        {
            ModelState.AddModelError(nameof(OrganisationName), "Enter a GitHub organisation or account.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
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
            await LoadAsync(cancellationToken);
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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var githubTask = gitHubHealthService.GetReportAsync(cancellationToken);
        var azureTask = azureHealthService.GetReportAsync(cancellationToken);
        await Task.WhenAll(githubTask, azureTask);
        GitHub = await githubTask;
        Azure = await azureTask;
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class GitHubCleanupModel(IPrototypeService prototypeService) : PageModel
{
    public IReadOnlyList<OrphanedGitHubRepository> OrphanedRepositories { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        OrphanedRepositories = await prototypeService.GetOrphanedGitHubRepositoriesAsync();
    }

    public async Task<IActionResult> OnPostMarkClearedAsync(Guid id)
    {
        try
        {
            await prototypeService.MarkTrackedGitHubRepositoryClearedAsync(id, User.GetEmail());
            StatusMessage = "Marked as cleaned up in GitHub.";
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }

        return RedirectToPage();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class GitHubHealthModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Admin/DependencyHealth");

    public IActionResult OnPost() => RedirectToPage("/Admin/DependencyHealth");
}

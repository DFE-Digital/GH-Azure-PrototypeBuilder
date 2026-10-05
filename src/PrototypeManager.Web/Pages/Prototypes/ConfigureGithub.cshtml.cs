using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class ConfigureGithubModel(
    IPrototypeService prototypeService,
    IProvisioningProgressService provisioningProgressService) : PageModel
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
            StatusMessage = "You do not have permission to retry GitHub deployment for this prototype.";
            return RedirectToPage("/Prototypes/Setup", new { id });
        }

        try
        {
            await prototypeService.ConfigureGitHubDeploymentAsync(id, User.GetEmail());
            StatusMessage = "GitHub deployment retry queued. This page will update automatically.";
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }

        var jobs = await prototypeService.GetJobsAsync(id);
        var auditEvents = await prototypeService.GetAuditEventsAsync(id);

        if (provisioningProgressService.ShouldShowProgress(prototype, jobs, auditEvents))
        {
            return RedirectToPage("/Prototypes/Setup", new { id });
        }

        return RedirectToPage("/Prototypes/Details", new { id, section = DetailsModel.GithubSecuritySection });
    }
}

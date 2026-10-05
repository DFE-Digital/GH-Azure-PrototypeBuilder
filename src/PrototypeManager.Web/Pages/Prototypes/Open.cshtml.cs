using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class OpenModel(
    IPrototypeService prototypeService,
    IPrototypeWebAppRuntimeService webAppRuntimeService,
    IPrototypeSiteAvailabilityChecker siteAvailabilityChecker) : PageModel
{
    public Prototype? Prototype { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (Prototype is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Prototype.PrototypeUrl))
        {
            return RedirectToPage("/Prototypes/Details", new { id });
        }

        return Page();
    }

    public async Task<IActionResult> OnGetReadyCheckAsync(Guid id)
    {
        Prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (Prototype is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Prototype.PrototypeUrl))
        {
            return new JsonResult(new { ready = false, message = "Prototype URL is not available yet." });
        }

        var runtimeStatus = await webAppRuntimeService.GetStatusAsync(Prototype);

        if (runtimeStatus.CanStart)
        {
            try
            {
                await webAppRuntimeService.StartAsync(Prototype);
                runtimeStatus = await webAppRuntimeService.GetStatusAsync(Prototype);
            }
            catch
            {
                // Continue polling — the app may still warm up.
            }
        }

        var availability = await siteAvailabilityChecker.CheckAsync(Prototype.PrototypeUrl);
        var ready = availability == PrototypeSiteAvailability.Ready;

        return new JsonResult(new
        {
            ready,
            powerState = runtimeStatus.Label,
            redirectUrl = ready ? Prototype.PrototypeUrl : null
        });
    }
}

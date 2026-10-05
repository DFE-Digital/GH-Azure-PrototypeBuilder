using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class PasswordModel(IPrototypeService prototypeService) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    public Prototype? Prototype { get; private set; }

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var prototype = await GetAccessiblePrototypeAsync(id);

        return prototype is null
            ? NotFound()
            : RedirectToPage("/Prototypes/Details", new { id, section = DetailsModel.PrototypeSection });
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        Prototype = await GetAccessiblePrototypeAsync(id);

        if (Prototype is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            ModelState.AddModelError(nameof(NewPassword), $"Prototype password must be at least {PrototypePasswordRules.MinimumLength} characters.");
            return Page();
        }

        try
        {
            PrototypePasswordRules.Validate(NewPassword);
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(nameof(NewPassword), exception.Message);
            return Page();
        }

        try
        {
            await prototypeService.ChangePasswordAsync(id, NewPassword, User.GetEmail());
            StatusMessage = "Password change queued.";
            return RedirectToPage("/Prototypes/Details", new { id, section = DetailsModel.PrototypeSection });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task<Prototype?> GetAccessiblePrototypeAsync(Guid id)
    {
        var prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (prototype is null)
        {
            return null;
        }

        var contacts = await prototypeService.GetContactsAsync(id);

        return PrototypeAccess.CanManagePrototype(
            prototype,
            contacts,
            User.GetEmail(),
            User.IsAdmin(),
            User.IsSupport())
            ? prototype
            : null;
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class ExtendModel(IPrototypeService prototypeService) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var prototype = await GetAccessiblePrototypeAsync(id);

        if (prototype is null)
        {
            return NotFound();
        }

        return RedirectToPage("/Prototypes/Details", new { id, section = DetailsModel.PrototypeSection });
    }

    public Task<IActionResult> OnPostAsync(Guid id) => OnGetAsync(id);

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

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class OwnerModel(IPrototypeService prototypeService) : PageModel
{
    public Prototype? Prototype { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (Prototype is null)
        {
            return NotFound();
        }

        if (!PrototypeAccess.CanManageContacts(Prototype, User.GetEmail(), User.IsAdmin(), User.IsSupport()))
        {
            return NotFound();
        }

        return Page();
    }
}

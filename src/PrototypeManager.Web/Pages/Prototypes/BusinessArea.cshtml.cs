using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class BusinessAreaModel(IPrototypeService prototypeService) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        return prototype is null
            ? NotFound()
            : RedirectToPage("/Prototypes/Details", new { id, section = DetailsModel.PrototypeSection });
    }
}

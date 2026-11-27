using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class PrototypesModel(IPrototypeService prototypeService) : PageModel
{
    public IReadOnlyList<Prototype> Prototypes { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Prototypes = (await prototypeService.GetAllAsync())
            .Where(prototype => prototype.Status != PrototypeStatus.Deleted)
            .OrderBy(prototype => prototype.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

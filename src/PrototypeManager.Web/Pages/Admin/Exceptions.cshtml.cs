using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class ExceptionsModel(IPrototypeService prototypeService) : PageModel
{
    public IReadOnlyList<ExceptionRequest> ExceptionRequests { get; private set; } = [];

    public async Task OnGetAsync()
    {
        ExceptionRequests = await prototypeService.GetExceptionRequestsAsync();
    }
}

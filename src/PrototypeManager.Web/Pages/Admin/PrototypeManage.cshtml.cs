using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class PrototypeManageModel(
    IPrototypeService prototypeService) : PageModel
{
    public Prototype? Prototype { get; private set; }
    public IReadOnlyList<PrototypeContact> Contacts { get; private set; } = [];
    public IReadOnlyList<ExceptionRequest> ExceptionRequests { get; private set; } = [];

    [BindProperty]
    public PlanInputModel PlanInput { get; set; } = new();

    [BindProperty]
    public string ConfirmDeleteName { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSetPlanAsync(Guid id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (!TryValidateModel(PlanInput, nameof(PlanInput)))
        {
            return Page();
        }

        try
        {
            var until = ResolveUntilDate(PlanInput);
            await prototypeService.SetTemporarySkuAsync(id, PlanInput.Sku, until, User.GetEmail());
            StatusMessage = $"Plan set to {PlanInput.Sku} until {until:dd MMM yyyy}.";
            return RedirectToPage(new { id });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRevertPlanAsync(Guid id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        try
        {
            await prototypeService.RevertTemporarySkuAsync(id, User.GetEmail());
            StatusMessage = "Plan reverted to Free.";
            return RedirectToPage(new { id });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveContactAsync(Guid id, Guid contactId)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        try
        {
            await prototypeService.RemoveManagerAsync(id, contactId, User.GetEmail(), isAdmin: true, isSupport: false);
            StatusMessage = "Prototype manager removed.";
            return RedirectToPage(new { id });
        }
        catch (Exception exception) when (exception is ValidationException or KeyNotFoundException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostClearDeputyAsync(Guid id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        try
        {
            await prototypeService.UpdateDeputyOwnerAsync(id, string.Empty, User.GetEmail(), isAdmin: true, isSupport: false);
            StatusMessage = "Deputy owner removed.";
            return RedirectToPage(new { id });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (!string.Equals(ConfirmDeleteName.Trim(), Prototype!.Name, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(ConfirmDeleteName), "Enter the prototype name exactly to confirm deletion.");
            return Page();
        }

        try
        {
            await prototypeService.DeleteAsync(id, User.GetEmail());
            StatusMessage = $"Deleted {Prototype.Name}.";
            return RedirectToPage("/Admin/Prototypes");
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task<bool> LoadAsync(Guid id)
    {
        Prototype = await prototypeService.GetAsync(id);
        if (Prototype is null || Prototype.Status == PrototypeStatus.Deleted)
        {
            return false;
        }

        Contacts = await prototypeService.GetContactsAsync(id);
        ExceptionRequests = await prototypeService.GetExceptionRequestsForPrototypeAsync(id);

        if (string.IsNullOrWhiteSpace(PlanInput.Sku))
        {
            PlanInput.Sku = "B1";
        }

        if (PlanInput.UntilDate is null && PlanInput.Days is null)
        {
            PlanInput.Days = 14;
        }

        return true;
    }

    private static DateTimeOffset ResolveUntilDate(PlanInputModel input)
    {
        if (input.UntilDate is not null)
        {
            var endOfDay = input.UntilDate.Value.Date.AddDays(1).AddTicks(-1);
            return new DateTimeOffset(DateTime.SpecifyKind(endOfDay, DateTimeKind.Utc));
        }

        var days = input.Days ?? 14;
        if (days < 1)
        {
            throw new ValidationException("Enter at least 1 day, or choose an until date.");
        }

        return DateTimeOffset.UtcNow.AddDays(days);
    }

    public sealed class PlanInputModel
    {
        [Required]
        [Display(Name = "Plan")]
        public string Sku { get; set; } = "B1";

        [Display(Name = "Number of days")]
        [Range(1, 90)]
        public int? Days { get; set; }

        [Display(Name = "Or until date")]
        [DataType(DataType.Date)]
        public DateTime? UntilDate { get; set; }
    }
}

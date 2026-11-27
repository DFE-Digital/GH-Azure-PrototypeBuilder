using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Web;

namespace PrototypeManager.Web.Pages;

public sealed class FeaturesModel(
    IFeatureSuggestionNotifier featureSuggestionNotifier,
    IOptions<NotifyOptions> notifyOptions) : PageModel
{
    [TempData]
    public string? StatusMessage { get; set; }

    public bool ShowSuggestModal { get; private set; }

    [BindProperty]
    [Required(ErrorMessage = "Enter what the feature is")]
    [StringLength(200, ErrorMessage = "Feature must be 200 characters or fewer")]
    [Display(Name = "What is the feature?")]
    public string Feature { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Enter what problem it would solve")]
    [StringLength(2000, ErrorMessage = "Problem must be 2000 characters or fewer")]
    [Display(Name = "What problem would it solve?")]
    public string Problem { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostSuggestAsync(CancellationToken cancellationToken)
    {
        ShowSuggestModal = true;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var signedInEmail = User.GetEmail();
        var suggestedBy = string.IsNullOrWhiteSpace(signedInEmail)
            ? User.GetDisplayName()
            : signedInEmail;
        if (string.IsNullOrWhiteSpace(suggestedBy))
        {
            suggestedBy = "Unknown user";
        }

        if (!notifyOptions.Value.IsConfigured)
        {
            ModelState.AddModelError(
                string.Empty,
                "Feature suggestions are unavailable right now because email is not configured. Email design.ops@education.gov.uk instead.");
            return Page();
        }

        try
        {
            await featureSuggestionNotifier.NotifyFeatureSuggestedAsync(
                new FeatureSuggestion(
                    Feature.Trim(),
                    Problem.Trim(),
                    suggestedBy),
                cancellationToken);

            StatusMessage = "Thanks — your feature suggestion has been sent to DesignOps.";
            return RedirectToPage();
        }
        catch (Exception)
        {
            ModelState.AddModelError(
                string.Empty,
                "We could not send your suggestion right now. Try again later, or email design.ops@education.gov.uk.");
            return Page();
        }
    }
}

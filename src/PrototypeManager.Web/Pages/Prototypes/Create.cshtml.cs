using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class CreateModel(
    IPrototypeService prototypeService,
    IAccessManagementService accessManagementService,
    IGitHubOrganisationService githubOrganisationService,
    IGitHubTokenIdentityService gitHubTokenIdentityService) : PageModel
{
    public IReadOnlyList<BusinessArea> AvailableBusinessAreas { get; private set; } = [];
    public IReadOnlyList<GitHubOrganisation> AllowedGitHubOrganisations { get; private set; } = [];
    public string? AuthenticatedGitHubLogin { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadPageDataAsync();

        if (AvailableBusinessAreas.Count == 1)
        {
            Input.BusinessAreaId = AvailableBusinessAreas[0].Id;
        }

        if (AllowedGitHubOrganisations.Count == 1)
        {
            Input.GitHubOrganisation = AllowedGitHubOrganisations[0].Name;
        }
        else if (!string.IsNullOrWhiteSpace(AuthenticatedGitHubLogin))
        {
            var matchingAccount = AllowedGitHubOrganisations.FirstOrDefault(organisation =>
                string.Equals(organisation.Name, AuthenticatedGitHubLogin, StringComparison.OrdinalIgnoreCase));

            if (matchingAccount is not null)
            {
                Input.GitHubOrganisation = matchingAccount.Name;
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadPageDataAsync();
        Input.GitHubOrganisation = Input.GitHubOrganisation.Trim();
        Input.GitHubRepositoryName = Input.GitHubRepositoryName.Trim().ToLowerInvariant();

        if (AvailableBusinessAreas.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "You are not assigned to any business areas. Contact an administrator.");
            return Page();
        }

        if (AllowedGitHubOrganisations.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "No GitHub organisations are configured. Contact an administrator.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.GitHubOrganisation))
        {
            ModelState.AddModelError(nameof(Input.GitHubOrganisation), "Select a GitHub organisation or account.");
        }
        else if (!AllowedGitHubOrganisations.Any(organisation =>
                     string.Equals(organisation.Name, Input.GitHubOrganisation, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(Input.GitHubOrganisation), "Select an allowed GitHub organisation.");
        }

        if (string.IsNullOrWhiteSpace(Input.GitHubRepositoryName))
        {
            Input.GitHubRepositoryName = Input.AppName.Trim().ToLowerInvariant();
        }

        if (!AvailableBusinessAreas.Any(area => area.Id == Input.BusinessAreaId))
        {
            ModelState.AddModelError(nameof(Input.BusinessAreaId), "Select a business area you are assigned to.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var prototype = await prototypeService.CreateScaffoldedAsync(new CreateScaffoldedPrototypeRequest
            {
                AppName = Input.AppName.Trim().ToLowerInvariant(),
                BusinessAreaId = Input.BusinessAreaId,
                GitHubOrganisation = Input.GitHubOrganisation,
                GitHubRepositoryName = Input.GitHubRepositoryName,
                PrototypePassword = Input.PrototypePassword,
                DeclarationAgreed = Input.DeclarationAgreed
            }, User.GetEmail());

            TempData["StatusMessage"] = "Prototype created. We are setting up your GitHub repository and hosting.";
            return RedirectToPage("/Prototypes/Setup", new { id = prototype.Id });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private async Task LoadPageDataAsync()
    {
        AvailableBusinessAreas = await accessManagementService.GetVisibleBusinessAreasAsync(User.GetEmail(), User.IsAdmin());
        AllowedGitHubOrganisations = await githubOrganisationService.GetAllAsync();
        AuthenticatedGitHubLogin = await gitHubTokenIdentityService.TryGetAuthenticatedLoginAsync();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Select a GitHub organisation or account.")]
        [Display(Name = "GitHub organisation or account")]
        public string GitHubOrganisation { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a prototype app name.")]
        [MaxLength(20)]
        [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Use lowercase letters, numbers and hyphens only.")]
        [Display(Name = "Prototype app name")]
        public string AppName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a repository name.")]
        [MaxLength(100)]
        [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Use lowercase letters, numbers and hyphens only.")]
        [Display(Name = "GitHub repository name")]
        public string GitHubRepositoryName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a password.")]
        [MinLength(PrototypePasswordRules.MinimumLength, ErrorMessage = "Password must be at least 3 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string PrototypePassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Select a business area.")]
        [Display(Name = "Business area")]
        public Guid BusinessAreaId { get; set; }

        [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm the prototype declaration before continuing.")]
        [Display(Name = "I confirm this prototype will not contain real user data and will not be used as a live service")]
        public bool DeclarationAgreed { get; set; }
    }
}

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
    IGitHubTokenIdentityService gitHubTokenIdentityService,
    ICompassCatalogService compassCatalogService) : PageModel
{
    public IReadOnlyList<BusinessArea> AvailableBusinessAreas { get; private set; } = [];
    public IReadOnlyList<GitHubOrganisation> AllowedGitHubOrganisations { get; private set; } = [];
    public IReadOnlyList<CompassWorkItemOption> CompassWorkItems { get; private set; } = [];
    public IReadOnlyList<CompassProductOption> CompassProducts { get; private set; } = [];
    public bool CompassConfigured { get; private set; }
    public string? AuthenticatedGitHubLogin { get; private set; }
    public string RepositoryNamePreview { get; private set; } = string.Empty;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadPageDataAsync();

        if (AvailableBusinessAreas.Count == 1)
        {
            Input.BusinessAreaId = AvailableBusinessAreas[0].Id;
        }

        var dfeDigital = AllowedGitHubOrganisations.FirstOrDefault(organisation =>
            string.Equals(organisation.Name, "DFE-Digital", StringComparison.OrdinalIgnoreCase));

        if (dfeDigital is not null)
        {
            Input.GitHubOrganisation = dfeDigital.Name;
        }
        else if (AllowedGitHubOrganisations.Count == 1)
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

        UpdateRepositoryPreview();
    }

    public bool UsesDfeDigitalOrganisation =>
        AllowedGitHubOrganisations.Any(organisation =>
            string.Equals(organisation.Name, "DFE-Digital", StringComparison.OrdinalIgnoreCase));

    public bool ShowGitHubOrganisationPicker =>
        AllowedGitHubOrganisations.Count > 1;

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadPageDataAsync();
        Input.GitHubOrganisation = Input.GitHubOrganisation.Trim();
        Input.AppName = SlugGenerator.NormaliseAppName(Input.AppName);
        ModelState.Remove("Input.AppName");
        if (string.IsNullOrWhiteSpace(Input.AppName))
        {
            ModelState.AddModelError("Input.AppName", "Enter a prototype app name.");
        }
        else
        {
            try
            {
                SlugGenerator.ValidateAppName(Input.AppName);
            }
            catch (ArgumentException exception)
            {
                ModelState.AddModelError("Input.AppName", exception.Message);
            }
        }

        UpdateRepositoryPreview();

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

        if (!AvailableBusinessAreas.Any(area => area.Id == Input.BusinessAreaId))
        {
            ModelState.AddModelError(nameof(Input.BusinessAreaId), "Select a business area you are assigned to.");
        }

        if (Input.CompassWorkItemId is int workItemId &&
            CompassWorkItems.All(item => item.Id != workItemId))
        {
            ModelState.AddModelError(nameof(Input.CompassWorkItemId), "Select a valid COMPASS work item.");
        }

        if (!string.IsNullOrWhiteSpace(Input.CompassProductId) &&
            CompassProducts.All(product =>
                !string.Equals(product.Id, Input.CompassProductId, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(Input.CompassProductId), "Select a valid COMPASS product.");
        }

        ModelState.Remove(nameof(Input.GitHubRepositoryName));

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var prototype = await prototypeService.CreateScaffoldedAsync(new CreateScaffoldedPrototypeRequest
            {
                AppName = Input.AppName,
                BusinessAreaId = Input.BusinessAreaId,
                GitHubOrganisation = Input.GitHubOrganisation,
                CompassWorkItemId = Input.CompassWorkItemId,
                CompassProductId = string.IsNullOrWhiteSpace(Input.CompassProductId) ? null : Input.CompassProductId,
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
        CompassConfigured = compassCatalogService.IsConfigured;
        CompassWorkItems = await compassCatalogService.GetActiveWorkItemsAsync();
        CompassProducts = await compassCatalogService.GetProductsAsync();
    }

    private void UpdateRepositoryPreview()
    {
        RepositoryNamePreview = string.IsNullOrWhiteSpace(Input.AppName)
            ? $"{PrototypeRepositoryNaming.Prefix}xxxx-"
            : $"{PrototypeRepositoryNaming.Prefix}xxxx-{Input.AppName}";
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Select a GitHub organisation or account.")]
        [Display(Name = "GitHub organisation or account")]
        public string GitHubOrganisation { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a prototype app name.")]
        [MaxLength(SlugGenerator.AppNameMaxLength)]
        [RegularExpression("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", ErrorMessage = "Start with a letter. Use lowercase letters, numbers and hyphens only.")]
        [Display(Name = "Prototype app name")]
        public string AppName { get; set; } = string.Empty;

        [Display(Name = "GitHub repository name")]
        public string GitHubRepositoryName { get; set; } = string.Empty;

        [Display(Name = "COMPASS work item")]
        public int? CompassWorkItemId { get; set; }

        [Display(Name = "COMPASS product")]
        public string? CompassProductId { get; set; }

        [Required(ErrorMessage = "Enter a password.")]
        [MinLength(PrototypePasswordRules.MinimumLength, ErrorMessage = "Password must be at least 3 characters.")]
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

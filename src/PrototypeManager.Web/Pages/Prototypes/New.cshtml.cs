using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class NewModel(
    IPrototypeService prototypeService,
    IAccessManagementService accessManagementService,
    IGitHubOrganisationService githubOrganisationService,
    IGitHubRepositorySearchService gitHubRepositorySearchService,
    IGitHubPrototypeCodebaseAssessor gitHubPrototypeCodebaseAssessor) : PageModel
{
    public IReadOnlyList<BusinessArea> AvailableBusinessAreas { get; private set; } = [];
    public IReadOnlyList<GitHubOrganisation> AllowedGitHubOrganisations { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadPageDataAsync();

        if (AvailableBusinessAreas.Count == 1)
        {
            Input.BusinessAreaId = AvailableBusinessAreas[0].Id;
        }
    }

    public async Task<IActionResult> OnGetSearchRepositoriesAsync(string? q, CancellationToken cancellationToken)
    {
        var results = await gitHubRepositorySearchService.SearchAsync(q ?? string.Empty, cancellationToken);
        var assessed = await Task.WhenAll(results.Select(async result =>
        {
            var assessment = await gitHubPrototypeCodebaseAssessor.AssessAsync(result.HtmlUrl, cancellationToken);
            return ToRepositoryPayload(result.Name, result.FullName, result.HtmlUrl, assessment);
        }));

        return new JsonResult(assessed);
    }

    public async Task<IActionResult> OnGetAssessRepositoryAsync(string? url, CancellationToken cancellationToken)
    {
        var assessment = await gitHubPrototypeCodebaseAssessor.AssessAsync(url ?? string.Empty, cancellationToken);
        return new JsonResult(ToAssessmentPayload(assessment));
    }

    private static object ToRepositoryPayload(
        string name,
        string fullName,
        string htmlUrl,
        PrototypeCodebaseAssessment assessment) =>
        new
        {
            name,
            fullName,
            htmlUrl,
            support = assessment.Support.ToString(),
            summary = assessment.Summary,
            kitVersion = assessment.KitVersion,
            govUkFrontendVersion = assessment.GovUkFrontendVersion,
            latestGovUkFrontendVersion = assessment.LatestGovUkFrontendVersion,
            isGovUkFrontendBehind = assessment.IsGovUkFrontendBehind
        };

    private static object ToAssessmentPayload(PrototypeCodebaseAssessment assessment) =>
        new
        {
            support = assessment.Support.ToString(),
            summary = assessment.Summary,
            kitVersion = assessment.KitVersion,
            govUkFrontendVersion = assessment.GovUkFrontendVersion,
            latestGovUkFrontendVersion = assessment.LatestGovUkFrontendVersion,
            isGovUkFrontendBehind = assessment.IsGovUkFrontendBehind
        };

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadPageDataAsync();
        Input.GitHubRepositoryUrl = PrototypeGitHubRepositoryUrls.Normalize(Input.GitHubRepositoryUrl);

        if (AvailableBusinessAreas.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "You are not assigned to any business areas. Contact an administrator.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.GitHubRepositoryUrl))
        {
            ModelState.AddModelError(nameof(Input.GitHubRepositoryUrl), "Enter or select a GitHub repository.");
        }
        else
        {
            try
            {
                await githubOrganisationService.ValidateRepositoryUrlAsync(Input.GitHubRepositoryUrl);
            }
            catch (ValidationException exception)
            {
                ModelState.AddModelError(nameof(Input.GitHubRepositoryUrl), exception.Message);
            }
        }

        if (!AvailableBusinessAreas.Any(area => area.Id == Input.BusinessAreaId))
        {
            ModelState.AddModelError(nameof(Input.BusinessAreaId), "Select a business area you are assigned to.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var projectName = PrototypeGitHubRepositoryUrls.TryGetRepositoryName(Input.GitHubRepositoryUrl)
            ?? Input.AppName.Trim();

        try
        {
            var prototype = await prototypeService.CreateAsync(new CreatePrototypeRequest
            {
                AppName = Input.AppName.Trim().ToLowerInvariant(),
                BusinessAreaId = Input.BusinessAreaId,
                Project = projectName,
                GitHubRepositoryUrl = Input.GitHubRepositoryUrl,
                PrototypePassword = Input.PrototypePassword,
                DeclarationAgreed = Input.DeclarationAgreed
            }, User.GetEmail());

            TempData["StatusMessage"] = "Prototype created and queued for provisioning.";
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
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter or select a GitHub repository.")]
        [Display(Name = "GitHub repository")]
        public string GitHubRepositoryUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a prototype app name.")]
        [MaxLength(SlugGenerator.AppNameMaxLength)]
        [RegularExpression("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", ErrorMessage = "Start with a letter. Use lowercase letters, numbers and hyphens only.")]
        [Display(Name = "Prototype app name")]
        public string AppName { get; set; } = string.Empty;

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

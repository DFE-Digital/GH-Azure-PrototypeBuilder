using Microsoft.AspNetCore.Http;

namespace PrototypeManager.Web;

public sealed record AppHelpSnippet(string Title, string BodyHtml, string? ContactHint = null);

public static class AppHelpContent
{
    public const string DesignOpsEmail = "design.ops@education.gov.uk";

    public static AppHelpSnippet Resolve(HttpRequest request) =>
        Resolve(request.Path.Value, request.Query);

    public static AppHelpSnippet Resolve(string? requestPath, IQueryCollection? query = null)
    {
        var path = string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath.TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        var section = query?["section"].ToString();

        // Dashboard
        if (path is "/" or "/Index")
        {
            return Snippet(
                "Prototype Manager",
                "Set up and host prototypes on DfE infrastructure without Azure portal access.",
                "Use <strong>Start a new prototype</strong> for a fresh Prototype Kit repo, or <strong>Host an existing prototype</strong> to connect a GitHub repository you already have.",
                "When you already have prototypes, this page becomes your dashboard for what’s live, in progress, or failed.");
        }

        // Guidance / getting started
        if (path.Equals("/GettingStarted", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Overview",
                "This is the start of the user guide: what Prototype Manager does and how to get a prototype live.",
                "Use the left navigation for other guide pages and technical documentation.");
        }

        if (path.Equals("/Features", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Features",
                "What Prototype Manager supports today, and planned capabilities such as templates, journeys, and branding options.",
                "Use <strong>Suggest a feature</strong> to email DesignOps. Use <strong>Create</strong> or <strong>Host</strong> to try what’s available now.");
        }

        if (path.Equals("/Guidance/CreateOrHost", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Create or host",
                "Choose <strong>Create</strong> for a new Prototype Kit repository, or <strong>Host</strong> if the GitHub repo already exists.",
                "Both paths provision Azure hosting and can wire up GitHub Actions deployment.");
        }

        if (path.Equals("/Guidance/FindAndOpen", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Find and open",
                "How to find prototypes on the dashboard and list, then open the live URL and manage the password.");
        }

        if (path.Equals("/Guidance/SetupAndDeploy", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Setup and deploy",
                "What happens after create or host: background setup steps, retries, and GitHub Actions deployment.");
        }

        if (path.Equals("/Guidance/Manage", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Manage a prototype",
                "Overview, Access, Activity, and Settings once a prototype exists.");
        }

        if (path.Equals("/Guidance/AdminAndLimits", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Admin and limits",
                "Administration tools, Free-tier hosting limits, and how to get help in the product.");
        }

        if (path.Equals("/Guidance/Technical", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "How it works",
                "Technical overview of the ASP.NET app, background jobs, Azure, and the GitHub App.",
                "Use the other technical pages for hosting, GitHub, and jobs/access detail.");
        }

        if (path.Equals("/Guidance/Technical/Azure", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Azure hosting",
                "How each prototype gets a resource group and web app on the shared Free App Service plan pool, plus temporary higher SKUs.");
        }

        if (path.Equals("/Guidance/Technical/GitHub", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "GitHub integration",
                "How the GitHub App creates repos, scaffolds the kit, assigns collaborators, and configures Actions secrets and workflows.");
        }

        if (path.Equals("/Guidance/Technical/Operations", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Jobs and access",
                "Provisioning job types, who can see and manage prototypes, and optional Notify and Compass integrations.");
        }

        if (path.StartsWith("/Guidance", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Guidance",
                "User guide and technical documentation for Prototype Manager.",
                "Use the left navigation to move between pages.");
        }

        // Create / host
        if (path.StartsWith("/Prototypes/Create", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Create a prototype",
                "Use this when you’re starting a brand new prototype.",
                "We create a private GitHub repository with the GOV.UK Prototype Kit, then provision Azure hosting.",
                "Use a short app name (lowercase letters, numbers, hyphens). Choose a business area and link COMPASS work and product when you can.",
                "Confirm the prototype will not hold real user data or run as a live service.");
        }

        if (path.StartsWith("/Prototypes/New", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Host an existing prototype",
                "Use this when the prototype code is already in GitHub.",
                "Search for a repository, check the Supported badge and GOV.UK Frontend version, then confirm the app name, password, and business area.",
                "Prefer the <code>main</code> branch and a Node Prototype Kit style app.");
        }

        // Setup and related actions
        if (path.StartsWith("/Prototypes/Setup", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Setup progress",
                "Provisioning runs in the background. This page shows each step as it completes.",
                "If a step fails, read the error, fix the cause (for example GitHub App permissions), then retry that step.",
                "You don’t need to keep the browser open, but staying here shows live updates.");
        }

        if (path.StartsWith("/Prototypes/RetryScaffold", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Retry GitHub setup",
                "Re-queues creating or scaffolding the GitHub repository and Prototype Kit files.",
                "Use this when repo create or kit push failed earlier.");
        }

        if (path.StartsWith("/Prototypes/Retry", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Retry hosting",
                "Re-queues Azure hosting for this prototype after a failed or incomplete provision.",
                "Check Activity or Setup for the previous error before retrying.");
        }

        if (path.StartsWith("/Prototypes/ConfigureGithub", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Configure GitHub deployment",
                "Queues writing the Azure publish profile secret and GitHub Actions workflow, and can trigger the first deploy.",
                "The GitHub App needs Secrets and Workflows write permission on the organisation installation.");
        }

        if (path.StartsWith("/Prototypes/Open", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Open prototype",
                "Starts the Azure web app if it’s stopped, then takes you to the live URL.",
                "You’ll need the prototype password. Change it from Overview if you’ve forgotten it and have permission.");
        }

        if (path.StartsWith("/Prototypes/Password", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Change password",
                "Set a new password for the hosted prototype. It updates the Azure app setting used by basic auth.",
                "The new password is not shown again after you save — store it safely before sharing.");
        }

        if (path.StartsWith("/Prototypes/Owner", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Change owner",
                "Transfer ownership of this prototype to another person.",
                "Only the current owner, or an admin/support user, can change ownership.");
        }

        if (path.StartsWith("/Prototypes/BusinessArea", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Change business area",
                "Move this prototype to another business area so the right teams can find it in the list filters.");
        }

        if (path.StartsWith("/Prototypes/Archive", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Archive prototype",
                "Archive when the prototype is no longer actively used but you want to keep the record.",
                "Archived prototypes leave the main live lists. Contact DesignOps if you’re unsure whether to archive or delete.");
        }

        if (path.StartsWith("/Prototypes/Delete", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Delete prototype",
                "Deletes Azure hosting for this prototype. This cannot be undone from the UI.",
                "GitHub repositories created by Prototype Manager are left in place for manual or Admin cleanup.",
                "Only people with delete permission (typically owner, admin, or support) can do this.");
        }

        // Details sections (same page, different ?section=)
        if (path.StartsWith("/Prototypes/Details", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveDetailsSection(section);
        }

        // Prototypes list
        if (path.StartsWith("/Prototypes/List", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Prototypes", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Prototypes",
                "Browse hosting environments you can access.",
                "Use the left filters for your prototypes, all prototypes, or a business area (team).",
                "Search and status chips narrow the list. <strong>Manage</strong> opens details; <strong>Open</strong> goes to the live URL when available.",
                "Each row shows the business area and a link to the GitHub repository when set.");
        }

        // Generic prototypes fallback
        if (path.StartsWith("/Prototypes", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Prototypes",
                "Create, host, and manage cost-controlled Azure hosting for GOV.UK Prototype Kit apps.",
                "Open a prototype for URL, password, deploy, access, and settings.");
        }

        // Admin
        if (path.Equals("/Admin", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Admin/Index", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Admin overview",
                "Summary of failed deployments, prototypes, and business areas across the service.",
                "Use the Admin left navigation for access, cleanup, and exception requests.");
        }

        if (path.StartsWith("/Admin/Users", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Business area access",
                "Assign people to business areas so they can see and manage prototypes for that team.",
                "Admins can see all prototypes; other users only see areas they’re assigned to (plus prototypes where they’re owner, deputy, or a contact).");
        }

        if (path.StartsWith("/Admin/Admins", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Prototype Manager admins",
                "Grant or remove full administrator access to Prototype Manager.",
                "Admins can manage access, temporary plans, deletions, and GitHub cleanup.");
        }

        if (path.StartsWith("/Admin/RemoveAccess", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Remove access",
                "Revoke Prototype Manager access when someone leaves DfE or no longer needs the service.",
                "Remove business area assignments and admin rights as appropriate.");
        }

        if (path.StartsWith("/Admin/PrototypeManage", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Manage prototype (admin)",
                "Admin tools for a single prototype: temporary SKU changes, managers, and destructive actions.",
                "Temporary B1/B2 plans are for short research spikes and revert automatically when they expire.");
        }

        if (path.StartsWith("/Admin/Prototypes", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Manage prototypes",
                "Admin list of prototypes for support actions such as temporary higher plans, removing managers, or deletion.",
                "Prefer the user’s own Manage screens when they can fix the issue themselves.");
        }

        if (path.StartsWith("/Admin/DependencyHealth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Admin/GitHubHealth", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Dependency health",
                "Shows whether this environment can use Azure hosting and GitHub, including subscription access, the Free plan pool, App permissions, and allowed organisations.",
                "Add or remove allowed GitHub organisations here. dfe-digital is the organisation the app is installed on. Credentials are never shown.");
        }

        if (path.StartsWith("/Admin/GitHubCleanup", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "GitHub cleanup",
                "Track GitHub repositories that Prototype Manager created or linked and that may need manual cleanup.",
                "Deleting a prototype removes Azure hosting but leaves GitHub repos in place by design.");
        }

        if (path.StartsWith("/Admin/Exceptions", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Exception requests",
                "Review requests for a higher Azure plan (for example B1/B2) when Free tier isn’t enough.",
                "DesignOps is emailed when a request is raised. Approve only for legitimate research or support needs.");
        }

        if (path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Administration",
                "Manage who can use Prototype Manager, remove access for leavers, clean up orphaned GitHub repositories, and review exception requests.");
        }

        if (path.StartsWith("/Error", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Something went wrong",
                "An unexpected error occurred. Try again, or return to the dashboard.",
                "If it keeps happening, email DesignOps with what you were doing and the time it failed.");
        }

        if (path.StartsWith("/Privacy", StringComparison.OrdinalIgnoreCase))
        {
            return Snippet(
                "Privacy",
                "How Prototype Manager handles personal data for hosting and access management.");
        }

        // Default
        return Snippet(
            "Prototype Manager",
            "Set up and host prototypes on DfE infrastructure without needing the Azure portal.",
            "Start with <strong>Create a prototype</strong> or <strong>Host an existing prototype</strong> if you already have a GitHub repository.",
            "Open <strong>Getting started</strong> for the full user guide and technical documentation.");
    }

    private static AppHelpSnippet ResolveDetailsSection(string? section) =>
        section?.Trim().ToLowerInvariant() switch
        {
            "github-security" => Snippet(
                "Deploy",
                "Connect GitHub Actions so pushes to <code>main</code> deploy to Azure automatically.",
                "Configure deployment to store the publish profile secret and add the workflow file. You can trigger the first deploy from here.",
                "If configuration fails, check the error on this page or Activity, then retry."),

            "contacts" => Snippet(
                "Access",
                "Manage who can see the password and manage this prototype.",
                "<strong>Owner</strong> and <strong>deputy</strong> are primary contacts. <strong>Contacts</strong> are named team members with manage access.",
                "People may also see this prototype through their business area assignment."),

            "history" => Snippet(
                "Activity",
                "Audit and job history for this prototype: provisioning, password changes, config updates, deployments, and other management actions.",
                "Use this when something failed and you need the sequence of events."),

            "settings" => Snippet(
                "Settings",
                "Edit allowed config vars (synced to the Azure web app), and use lifecycle actions such as archive or delete.",
                "System settings are shown but not freely editable. Retry failed provisioning from here when the action is available."),

            _ => Snippet(
                "Overview",
                "Status, business area, live URL, and password (if you’re allowed).",
                "Open the app, change the password, or start/stop the web app when it’s live.",
                "Prototypes start on Free (F1). For significant concurrent research that may exceed Free limits, use <strong>Request higher plan</strong> — DesignOps reviews each request (B1 or B2).")
        };

    private static AppHelpSnippet Snippet(string title, params string[] paragraphs) =>
        new(title, string.Concat(paragraphs.Select(paragraph => $"<p>{paragraph}</p>")));
}

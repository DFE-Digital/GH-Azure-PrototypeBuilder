using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;

namespace PrototypeManager.Web.Pages;

public sealed class LiveModel(
    ILiveOperationsService liveOperationsService,
    IProvisioningProgressService provisioningProgressService) : PageModel
{
    public LivePageSnapshot Snapshot { get; private set; } = LivePageSnapshot.Empty;

    public string UserEmail => User.GetEmail();

    public async Task OnGetAsync()
    {
        Snapshot = await BuildSnapshotAsync();
    }

    public async Task<IActionResult> OnGetFeedAsync()
    {
        var snapshot = await BuildSnapshotAsync();
        return new JsonResult(snapshot.ToJson());
    }

    private async Task<LivePageSnapshot> BuildSnapshotAsync()
    {
        var data = await liveOperationsService.GetDataAsync(UserEmail, User.IsAdmin(), User.IsSupport());
        return LivePageSnapshotBuilder.Build(data, provisioningProgressService, Url);
    }
}

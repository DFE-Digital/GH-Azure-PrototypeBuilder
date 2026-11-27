using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PrototypeManager.Web.Pages.Account;

public sealed class SignOutModel(IHostEnvironment environment) : PageModel
{
    public IActionResult OnPost()
    {
        if (environment.IsDevelopment())
        {
            return RedirectToPage("/Account/SignedOut");
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = Url.Page("/Account/SignedOut") },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}

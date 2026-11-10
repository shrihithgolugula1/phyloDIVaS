using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CapstoneDesign.Controllers
{
    public class AccountController : Controller
    {
        [HttpGet]
        public IActionResult Login(string returnURL = "/")
        {
            return Challenge(new AuthenticationProperties { RedirectUri = returnURL },
                OpenIdConnectDefaults.AuthenticationScheme);
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            // Sign out local cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Sign out from Cognito hosted UI
            var cognitoSignOutUrl = "https://us-east-25sl9t1bgd.auth.us-east-2.amazoncognito.com/logout?client_id=4801a0g2hi7dkqtun3hsh3cf7r&logout_uri=https://localhost:7175/";
            return Redirect(cognitoSignOutUrl);

        }



   /*     private readonly SignInManager<CognitoUser> _signInManager;
        private readonly UserManager<CognitoUser> _userManager;

        public AccountController(SignInManager<CognitoUser> signInManager, UserManager<CognitoUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }  

        [HttpPost]
        public async Task<IActionResult> LoginSubmit(LoginViewModel model)
        {
            var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, false);
            if (result.Succeeded)
            {
                return RedirectToAction("HomePage", "Capstone");   //redirect to HomePage action in CapstoneController
            }
            return View(model);
        }

        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("HomePage", "Capstone");
        }

    /*    public IActionResult Login()
        {
            return View();
        }        */
    }
}

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/account/login", LoginHandler);
    }

    public static async Task<IResult> LoginHandler(
        [FromForm] LoginModel loginModel,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory, 
        HttpContext context)
    {
        var user = await signInManager.UserManager.FindByEmailAsync(loginModel.Email);

        if (user == null)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager.PasswordSignInAsync(user, loginModel.Password, true, false);

        if (!result.Succeeded)
        {
            return Results.Unauthorized();
        }

        var principal = await claimsFactory.CreateAsync(user);
        await context.SignInAsync(IdentityConstants.ApplicationScheme, principal);

        return Results.Redirect(loginModel.Redirect);
    }

    public class LoginModel
    {
        public required string Redirect { get; set; }
        public required string Email { get; set; }
        public required string Password { get; set; }
    }
}
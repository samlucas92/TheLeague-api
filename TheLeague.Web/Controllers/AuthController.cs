using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Web.Extensions;
using TheLeague.Web.Services;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IUserService userService, AuthTokenService authTokenService) : ControllerBase
{
	[HttpPost("register")]
	public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
	{
		var account = await userService.RegisterAsync(request.Name, request.EmailAddress, request.Password, cancellationToken);
		await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authTokenService.CreateClaimsPrincipal(account.Id));
		return Ok(authTokenService.ToAuthenticatedUser(account));
	}

	[HttpPost("login")]
	public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
	{
		var account = await userService.ValidateCredentialsAsync(request.EmailAddress, request.Password, cancellationToken);
		if (account is null)
		{
			return Unauthorized(new { error = "Email address or password is incorrect." });
		}

		await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authTokenService.CreateClaimsPrincipal(account.Id));
		return Ok(authTokenService.ToAuthenticatedUser(account));
	}

	[Authorize]
	[HttpPost("signout")]
	public async Task<IActionResult> Signout()
	{
		await HttpContext.SignOutAsync();
		return NoContent();
	}

	[Authorize]
	[HttpGet("me")]
	public async Task<IActionResult> Me(CancellationToken cancellationToken)
	{
		var account = await userService.GetByIdAsync(User.GetRequiredUserId(), cancellationToken);
		return account is null ? Unauthorized() : Ok(authTokenService.ToAuthenticatedUser(account));
	}

	[Authorize]
	[HttpPost("change-password")]
	public IActionResult ChangePassword() => StatusCode(StatusCodes.Status501NotImplemented);

	[HttpPost("forgot-password")]
	public IActionResult ForgotPassword() => Accepted();

	[HttpPost("reset-password")]
	public IActionResult ResetPassword() => StatusCode(StatusCodes.Status501NotImplemented);
}

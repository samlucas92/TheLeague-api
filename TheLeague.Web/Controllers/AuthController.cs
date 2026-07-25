using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Web.Extensions;
using TheLeague.Web.Security;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IUserService userService, IJwtTokenService jwtTokenService) : ControllerBase
{
	[AllowAnonymous]
	[HttpPost("register")]
	public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
	{
		var account = await userService.RegisterAsync(request.Name, request.EmailAddress, request.Password, cancellationToken);
		return Ok(jwtTokenService.CreateLoginResponse(account));
	}

	[AllowAnonymous]
	[HttpPost("login")]
	public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
	{
		var account = await userService.ValidateCredentialsAsync(request.EmailAddress, request.Password, cancellationToken);
		if (account is null)
		{
			return Unauthorized(new { error = "Email address or password is incorrect." });
		}

		return Ok(jwtTokenService.CreateLoginResponse(account));
	}

	[Authorize]
	[HttpPost("signout")]
	public IActionResult Signout() => NoContent();

	[Authorize]
	[HttpGet("me")]
	public async Task<IActionResult> Me(CancellationToken cancellationToken)
	{
		var account = await userService.GetByIdAsync(User.GetRequiredUserId(), cancellationToken);
		return account is null ? Unauthorized() : Ok(new AuthenticatedUser(account.Id, account.Name, account.EmailAddress));
	}

	[Authorize]
	[HttpPost("change-password")]
	public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
	{
		await userService.ChangePasswordAsync(User.GetRequiredUserId(), request.CurrentPassword, request.NewPassword, cancellationToken);
		return NoContent();
	}

	[HttpPost("forgot-password")]
	public IActionResult ForgotPassword() => Accepted();

	[HttpPost("reset-password")]
	public IActionResult ResetPassword() => StatusCode(StatusCodes.Status501NotImplemented);
}

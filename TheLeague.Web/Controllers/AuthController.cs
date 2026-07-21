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
		var response = jwtTokenService.CreateLoginResponse(account);
		SetAuthCookie(response);
		return Ok(response);
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

		var response = jwtTokenService.CreateLoginResponse(account);
		SetAuthCookie(response);
		return Ok(response);
	}

	[Authorize]
	[HttpPost("signout")]
	public IActionResult Signout()
	{
		Response.Cookies.Delete("theleague.accessToken", GetCookieOptions(DateTimeOffset.UtcNow.AddDays(-1)));
		return NoContent();
	}

	[Authorize]
	[HttpGet("me")]
	public async Task<IActionResult> Me(CancellationToken cancellationToken)
	{
		var account = await userService.GetByIdAsync(User.GetRequiredUserId(), cancellationToken);
		return account is null ? Unauthorized() : Ok(new AuthenticatedUser(account.Id, account.Name, account.EmailAddress));
	}

	[Authorize]
	[HttpPost("change-password")]
	public IActionResult ChangePassword() => StatusCode(StatusCodes.Status501NotImplemented);

	[HttpPost("forgot-password")]
	public IActionResult ForgotPassword() => Accepted();

	[HttpPost("reset-password")]
	public IActionResult ResetPassword() => StatusCode(StatusCodes.Status501NotImplemented);

	private void SetAuthCookie(LoginResponse response)
	{
		Response.Cookies.Append("theleague.accessToken", response.Token, GetCookieOptions(new DateTimeOffset(response.ExpiresAt, TimeSpan.Zero)));
	}

	private CookieOptions GetCookieOptions(DateTimeOffset expires) => new()
	{
		HttpOnly = true,
		SameSite = SameSiteMode.None,
		Secure = IsSecureCookieHost(),
		Expires = expires,
		Path = "/"
	};

	private bool IsSecureCookieHost()
	{
		if (Request.IsHttps)
		{
			return true;
		}

		return !Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
			!Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
	}
}

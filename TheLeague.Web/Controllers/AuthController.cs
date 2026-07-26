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
public class AuthController(IUserService userService, IJwtTokenService jwtTokenService, IEmailOutboxService emailOutboxService) : ControllerBase
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

	[AllowAnonymous]
	[HttpPost("forgot-password")]
	public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
	{
		var result = await userService.RequestPasswordResetAsync(request.EmailAddress, cancellationToken);
		if (result.ResetToken is not null)
		{
			var resetLink = BuildResetLink(result.ResetToken);
			var message = await emailOutboxService.QueueAsync(
				request.EmailAddress,
				null,
				"Reset your The League password",
				BuildPasswordResetHtml(resetLink),
				$"Reset your The League password: {resetLink}",
				cancellationToken);
			await emailOutboxService.SendAsync(message.Id, cancellationToken);
		}

		return Accepted(new ForgotPasswordResponse("If that email is registered, a password reset email has been sent.", null, result.ExpiresAt));
	}

	[AllowAnonymous]
	[HttpPost("reset-password")]
	public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
	{
		await userService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
		return NoContent();
	}

	private string BuildResetLink(string token)
	{
		var origin = Request.Headers.Origin.FirstOrDefault();
		if (string.IsNullOrWhiteSpace(origin))
		{
			origin = $"{Request.Scheme}://{Request.Host}";
		}

		return $"{origin.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
	}

	private static string BuildPasswordResetHtml(string resetLink) =>
		$"""
		<p>You asked to reset your The League password.</p>
		<p><a href="{resetLink}">Reset your password</a></p>
		<p>If you did not request this, you can ignore this email.</p>
		""";
}

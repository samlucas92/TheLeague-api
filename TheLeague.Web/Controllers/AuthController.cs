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
		await QueueVerificationEmailAsync(account, cancellationToken);
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
		return account is null ? Unauthorized() : Ok(new AuthenticatedUser(account.Id, account.Name, account.EmailAddress, account.IsEmailVerified));
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

	[Authorize]
	[HttpPost("email-verification")]
	public async Task<IActionResult> ResendEmailVerification(CancellationToken cancellationToken)
	{
		var account = await userService.GetByIdAsync(User.GetRequiredUserId(), cancellationToken);
		if (account is null)
		{
			return Unauthorized();
		}

		if (account.IsEmailVerified)
		{
			return Ok(new { message = "Your email is already verified." });
		}

		await QueueVerificationEmailAsync(account, cancellationToken);
		return Accepted(new { message = "Verification email sent." });
	}

	[AllowAnonymous]
	[HttpPost("verify-email")]
	public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken)
	{
		await userService.VerifyEmailAsync(request.Token, cancellationToken);
		return NoContent();
	}

	private async Task QueueVerificationEmailAsync(UserAccount account, CancellationToken cancellationToken)
	{
		var result = await userService.RequestEmailVerificationAsync(account.Id, cancellationToken);
		if (result.VerificationToken is null)
		{
			return;
		}

		var verificationLink = BuildEmailVerificationLink(result.VerificationToken);
		var message = await emailOutboxService.QueueAsync(
			account.EmailAddress,
			account.Name,
			"Verify your The League email",
			BuildEmailVerificationHtml(verificationLink),
			$"Verify your The League email: {verificationLink}",
			cancellationToken);
		await emailOutboxService.SendAsync(message.Id, cancellationToken);
	}

	private string BuildResetLink(string token)
	{
		return BuildFrontendLink("reset-password", token);
	}

	private string BuildEmailVerificationLink(string token)
	{
		return BuildFrontendLink("verify-email", token);
	}

	private string BuildFrontendLink(string path, string token)
	{
		var origin = Request.Headers.Origin.FirstOrDefault();
		if (string.IsNullOrWhiteSpace(origin))
		{
			origin = $"{Request.Scheme}://{Request.Host}";
		}

		return $"{origin.TrimEnd('/')}/{path}?token={Uri.EscapeDataString(token)}";
	}

	private static string BuildPasswordResetHtml(string resetLink) =>
		$"""
		<p>You asked to reset your The League password.</p>
		<p><a href="{resetLink}">Reset your password</a></p>
		<p>If you did not request this, you can ignore this email.</p>
		""";

	private static string BuildEmailVerificationHtml(string verificationLink) =>
		$"""
		<p>Welcome to The League.</p>
		<p><a href="{verificationLink}">Verify your email address</a></p>
		<p>If you did not create this account, you can ignore this email.</p>
		""";
}

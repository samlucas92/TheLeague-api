using System.Security.Cryptography;
using System.Text;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class UserService(LeagueDataContext context) : IUserService
{
	public Task<UserAccount> RegisterAsync(string name, string emailAddress, string password, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new InvalidOperationException("Name is required.");
		}

		if (password.Length < 8)
		{
			throw new InvalidOperationException("Password must be at least 8 characters.");
		}

		var normalizedEmail = NormalizeEmail(emailAddress);
		if (context.Users.Values.Any(user => EmailMatches(user.EmailAddress, normalizedEmail)))
		{
			throw new InvalidOperationException("Email address is already registered.");
		}

		var account = new UserAccount
		{
			Id = Guid.NewGuid(),
			Name = name.Trim(),
			EmailAddress = normalizedEmail,
			PasswordHash = PasswordHasher.Hash(password),
			CreatedAt = DateTime.UtcNow
		};

		context.Users[account.Id] = account;

		return Task.FromResult(account);
	}

	public Task<UserAccount?> ValidateCredentialsAsync(string emailAddress, string password, CancellationToken cancellationToken = default)
	{
		var normalizedEmail = NormalizeEmail(emailAddress);
		var account = context.Users.Values
			.Where(user => EmailMatches(user.EmailAddress, normalizedEmail))
			.OrderByDescending(user => user.CreatedAt)
			.FirstOrDefault(user => PasswordHasher.Verify(password, user.PasswordHash));

		return Task.FromResult(account);
	}

	public Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		context.Users.TryGetValue(userId, out var account);
		return Task.FromResult(account);
	}

	public Task<UserAccount?> GetByEmailAsync(string emailAddress, CancellationToken cancellationToken = default)
	{
		var normalizedEmail = NormalizeEmail(emailAddress);
		return Task.FromResult(context.Users.Values
			.Where(user => EmailMatches(user.EmailAddress, normalizedEmail))
			.OrderByDescending(user => user.CreatedAt)
			.FirstOrDefault());
	}

	public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
	{
		if (!context.Users.TryGetValue(userId, out var account))
		{
			throw new UnauthorizedAccessException("Please sign in again.");
		}

		if (!PasswordHasher.Verify(currentPassword, account.PasswordHash))
		{
			throw new InvalidOperationException("Current password is incorrect.");
		}

		if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
		{
			throw new InvalidOperationException("New password must be at least 8 characters.");
		}

		if (PasswordHasher.Verify(newPassword, account.PasswordHash))
		{
			throw new InvalidOperationException("New password must be different from your current password.");
		}

		account.PasswordHash = PasswordHasher.Hash(newPassword);
		await context.Users.SaveAsync(account, cancellationToken);
	}

	public async Task<ForgotPasswordResult> RequestPasswordResetAsync(string emailAddress, CancellationToken cancellationToken = default)
	{
		var normalizedEmail = NormalizeEmail(emailAddress);
		var account = context.Users.Values
			.Where(user => EmailMatches(user.EmailAddress, normalizedEmail))
			.OrderByDescending(user => user.CreatedAt)
			.FirstOrDefault();

		if (account is null)
		{
			return new ForgotPasswordResult(false, null, null);
		}

		foreach (var existingToken in context.PasswordResetTokens.Values.Where(token => token.UserId == account.Id && token.UsedAt is null && token.ExpiresAt > DateTime.UtcNow))
		{
			existingToken.UsedAt = DateTime.UtcNow;
			await context.PasswordResetTokens.SaveAsync(existingToken, cancellationToken);
		}

		var resetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
			.Replace("+", "-")
			.Replace("/", "_")
			.TrimEnd('=');
		var expiresAt = DateTime.UtcNow.AddHours(1);
		var token = new PasswordResetToken
		{
			Id = Guid.NewGuid(),
			UserId = account.Id,
			TokenHash = HashResetToken(resetToken),
			ExpiresAt = expiresAt,
			CreatedAt = DateTime.UtcNow
		};

		context.PasswordResetTokens[token.Id] = token;
		return new ForgotPasswordResult(true, resetToken, expiresAt);
	}

	public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException("Reset token is required.");
		}

		if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
		{
			throw new InvalidOperationException("New password must be at least 8 characters.");
		}

		var tokenHash = HashResetToken(token.Trim());
		var resetToken = context.PasswordResetTokens.Values
			.Where(candidate => candidate.TokenHash == tokenHash)
			.OrderByDescending(candidate => candidate.CreatedAt)
			.FirstOrDefault();
		if (resetToken is null || resetToken.UsedAt.HasValue || resetToken.ExpiresAt <= DateTime.UtcNow)
		{
			throw new InvalidOperationException("Reset link is invalid or has expired.");
		}

		if (!context.Users.TryGetValue(resetToken.UserId, out var account))
		{
			throw new InvalidOperationException("Reset link is invalid or has expired.");
		}

		account.PasswordHash = PasswordHasher.Hash(newPassword);
		resetToken.UsedAt = DateTime.UtcNow;
		await context.Users.SaveAsync(account, cancellationToken);
		await context.PasswordResetTokens.SaveAsync(resetToken, cancellationToken);
	}

	public async Task<EmailVerificationResult> RequestEmailVerificationAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (!context.Users.TryGetValue(userId, out var account))
		{
			throw new UnauthorizedAccessException("Please sign in again.");
		}

		if (account.IsEmailVerified)
		{
			return new EmailVerificationResult(false, null, account.EmailVerifiedAt);
		}

		foreach (var existingToken in context.EmailVerificationTokens.Values.Where(token => token.UserId == account.Id && token.UsedAt is null && token.ExpiresAt > DateTime.UtcNow))
		{
			existingToken.UsedAt = DateTime.UtcNow;
			await context.EmailVerificationTokens.SaveAsync(existingToken, cancellationToken);
		}

		var verificationToken = CreateToken();
		var expiresAt = DateTime.UtcNow.AddHours(24);
		var token = new EmailVerificationToken
		{
			Id = Guid.NewGuid(),
			UserId = account.Id,
			TokenHash = HashToken(verificationToken),
			ExpiresAt = expiresAt,
			CreatedAt = DateTime.UtcNow
		};

		context.EmailVerificationTokens[token.Id] = token;
		return new EmailVerificationResult(true, verificationToken, expiresAt);
	}

	public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException("Verification token is required.");
		}

		var tokenHash = HashToken(token.Trim());
		var verificationToken = context.EmailVerificationTokens.Values
			.Where(candidate => candidate.TokenHash == tokenHash)
			.OrderByDescending(candidate => candidate.CreatedAt)
			.FirstOrDefault();
		if (verificationToken is null || verificationToken.UsedAt.HasValue || verificationToken.ExpiresAt <= DateTime.UtcNow)
		{
			throw new InvalidOperationException("Verification link is invalid or has expired.");
		}

		if (!context.Users.TryGetValue(verificationToken.UserId, out var account))
		{
			throw new InvalidOperationException("Verification link is invalid or has expired.");
		}

		account.IsEmailVerified = true;
		account.EmailVerifiedAt = DateTime.UtcNow;
		verificationToken.UsedAt = DateTime.UtcNow;
		await context.Users.SaveAsync(account, cancellationToken);
		await context.EmailVerificationTokens.SaveAsync(verificationToken, cancellationToken);
	}

	internal static string NormalizeEmail(string emailAddress)
	{
		if (string.IsNullOrWhiteSpace(emailAddress) || !emailAddress.Contains('@'))
		{
			throw new InvalidOperationException("A valid email address is required.");
		}

		return emailAddress.Trim().ToLowerInvariant();
	}

	private static bool EmailMatches(string storedEmailAddress, string normalizedEmailAddress) =>
		string.Equals(storedEmailAddress?.Trim(), normalizedEmailAddress, StringComparison.OrdinalIgnoreCase);

	private static string CreateToken() =>
		Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
			.Replace("+", "-")
			.Replace("/", "_")
			.TrimEnd('=');

	private static string HashResetToken(string token)
	{
		return HashToken(token);
	}

	private static string HashToken(string token)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
		return Convert.ToHexString(bytes);
	}
}

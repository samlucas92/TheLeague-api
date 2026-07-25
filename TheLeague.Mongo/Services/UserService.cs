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
}

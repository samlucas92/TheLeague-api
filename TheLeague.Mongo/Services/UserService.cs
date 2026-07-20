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
		if (context.Users.Values.Any(user => user.EmailAddress == normalizedEmail))
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
		var account = context.Users.Values.SingleOrDefault(user => user.EmailAddress == normalizedEmail);

		return Task.FromResult(account is not null && PasswordHasher.Verify(password, account.PasswordHash) ? account : null);
	}

	public Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		context.Users.TryGetValue(userId, out var account);
		return Task.FromResult(account);
	}

	public Task<UserAccount?> GetByEmailAsync(string emailAddress, CancellationToken cancellationToken = default)
	{
		var normalizedEmail = NormalizeEmail(emailAddress);
		return Task.FromResult(context.Users.Values.SingleOrDefault(user => user.EmailAddress == normalizedEmail));
	}

	internal static string NormalizeEmail(string emailAddress)
	{
		if (string.IsNullOrWhiteSpace(emailAddress) || !emailAddress.Contains('@'))
		{
			throw new InvalidOperationException("A valid email address is required.");
		}

		return emailAddress.Trim().ToLowerInvariant();
	}
}

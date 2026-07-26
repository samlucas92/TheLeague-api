using Microsoft.Extensions.Options;
using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class EmailOutboxService(LeagueDataContext context, IEmailSender sender, IOptions<EmailSettings> emailSettings) : IEmailOutboxService
{
	public Task<IReadOnlyCollection<EmailAuditItem>> ListRecentAsync(int take = 50, CancellationToken cancellationToken = default)
	{
		var items = context.EmailMessages.Values
			.OrderByDescending(message => message.CreatedAt)
			.Take(Math.Clamp(take, 1, 200))
			.Select(message => new EmailAuditItem(
				message.Id,
				message.Provider,
				message.Status.ToString(),
				message.ToEmailAddress,
				message.ToName,
				message.Subject,
				message.Attempts,
				message.ProviderMessageId,
				message.FailureReason,
				message.CreatedAt,
				message.UpdatedAt,
				message.SentAt,
				message.NextAttemptAt))
			.ToArray();

		return Task.FromResult<IReadOnlyCollection<EmailAuditItem>>(items);
	}

	public async Task<EmailMessage> QueueAsync(string toEmailAddress, string? toName, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default)
	{
		var settings = emailSettings.Value;
		var now = DateTime.UtcNow;
		var message = new EmailMessage
		{
			Provider = string.IsNullOrWhiteSpace(settings.Provider) ? "Resend" : settings.Provider.Trim(),
			Status = EmailMessageStatus.Pending,
			ToEmailAddress = toEmailAddress.Trim(),
			ToName = string.IsNullOrWhiteSpace(toName) ? null : toName.Trim(),
			FromEmailAddress = settings.FromEmail.Trim(),
			FromName = string.IsNullOrWhiteSpace(settings.FromName) ? null : settings.FromName.Trim(),
			Subject = subject.Trim(),
			HtmlBody = htmlBody,
			TextBody = textBody,
			CreatedAt = now,
			UpdatedAt = now
		};

		context.EmailMessages[message.Id] = message;
		await context.EmailMessages.SaveAsync(message, cancellationToken);
		return message;
	}

	public async Task<EmailMessage> SendAsync(Guid emailMessageId, CancellationToken cancellationToken = default)
	{
		if (!context.EmailMessages.TryGetValue(emailMessageId, out var message))
		{
			throw new InvalidOperationException("Email message was not found.");
		}

		message.Status = EmailMessageStatus.Sending;
		message.Attempts++;
		message.UpdatedAt = DateTime.UtcNow;
		message.FailureReason = null;
		await context.EmailMessages.SaveAsync(message, cancellationToken);

		var result = await sender.SendAsync(message, cancellationToken);
		message.UpdatedAt = DateTime.UtcNow;
		if (result.Succeeded)
		{
			message.Status = EmailMessageStatus.Sent;
			message.ProviderMessageId = result.ProviderMessageId;
			message.SentAt = DateTime.UtcNow;
			message.NextAttemptAt = null;
			message.FailureReason = null;
		}
		else
		{
			message.Status = EmailMessageStatus.Failed;
			message.FailureReason = result.FailureReason ?? "Email provider did not accept the message.";
			message.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, message.Attempts - 1))));
		}

		await context.EmailMessages.SaveAsync(message, cancellationToken);
		return message;
	}
}

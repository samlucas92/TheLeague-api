using TheLeague.Enums;

namespace TheLeague.Models;

public class EmailMessage
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Provider { get; set; } = "Resend";
	public EmailMessageStatus Status { get; set; } = EmailMessageStatus.Pending;
	public string ToEmailAddress { get; set; } = string.Empty;
	public string? ToName { get; set; }
	public string FromEmailAddress { get; set; } = string.Empty;
	public string? FromName { get; set; }
	public string Subject { get; set; } = string.Empty;
	public string HtmlBody { get; set; } = string.Empty;
	public string TextBody { get; set; } = string.Empty;
	public int Attempts { get; set; }
	public string? ProviderMessageId { get; set; }
	public string? FailureReason { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
	public DateTime? SentAt { get; set; }
	public DateTime? NextAttemptAt { get; set; }
}

namespace TheLeague.Models;

public class EmailSettings
{
	public string Provider { get; set; } = "Resend";
	public string FromEmail { get; set; } = string.Empty;
	public string FromName { get; set; } = "The League";
}

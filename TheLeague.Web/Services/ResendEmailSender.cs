using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Services;

public class ResendEmailSender(HttpClient httpClient, IOptions<ResendSettings> resendSettings, IOptions<EmailSettings> emailSettings) : IEmailSender
{
	public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
	{
		var apiKey = resendSettings.Value.ApiKey;
		var fromEmail = emailSettings.Value.FromEmail;

		if (string.IsNullOrWhiteSpace(apiKey))
		{
			return new EmailSendResult(false, null, "Resend API key is not configured.");
		}

		if (string.IsNullOrWhiteSpace(fromEmail))
		{
			return new EmailSendResult(false, null, "Email from address is not configured.");
		}

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
			request.Content = JsonContent.Create(new ResendEmailRequest(
				BuildFrom(message),
				[message.ToEmailAddress],
				message.Subject,
				message.HtmlBody,
				message.TextBody));

			using var response = await httpClient.SendAsync(request, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return new EmailSendResult(false, null, $"Resend returned {(int)response.StatusCode}: {body}");
			}

			var result = await response.Content.ReadFromJsonAsync<ResendEmailResponse>(cancellationToken);
			return new EmailSendResult(true, result?.Id, null);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
		{
			return new EmailSendResult(false, null, exception.Message);
		}
	}

	private static string BuildFrom(EmailMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.FromName))
		{
			return message.FromEmailAddress;
		}

		return $"{message.FromName} <{message.FromEmailAddress}>";
	}

	private record ResendEmailRequest(
		[property: JsonPropertyName("from")] string From,
		[property: JsonPropertyName("to")] IReadOnlyCollection<string> To,
		[property: JsonPropertyName("subject")] string Subject,
		[property: JsonPropertyName("html")] string Html,
		[property: JsonPropertyName("text")] string Text);

	private record ResendEmailResponse([property: JsonPropertyName("id")] string? Id);
}

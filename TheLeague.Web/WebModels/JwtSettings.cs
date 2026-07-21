namespace TheLeague.Web.WebModels;

public sealed class JwtSettings
{
	public string Issuer { get; set; } = "TheLeague";

	public string Audience { get; set; } = "TheLeague.Frontend";

	public string Secret { get; set; } = string.Empty;

	public int ExpiryMinutes { get; set; } = 480;
}

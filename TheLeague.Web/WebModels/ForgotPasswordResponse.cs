namespace TheLeague.Web.WebModels;

public record ForgotPasswordResponse(string Message, string? ResetLink, DateTime? ExpiresAt);

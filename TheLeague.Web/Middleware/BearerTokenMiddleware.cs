using TheLeague.Web.Services;

namespace TheLeague.Web.Middleware;

public class BearerTokenMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context, AuthTokenService authTokenService)
	{
		if (context.User.Identity?.IsAuthenticated != true &&
			TryGetBearerToken(context.Request, out var accessToken) &&
			authTokenService.TryValidateAccessToken(accessToken, out var userId))
		{
			context.User = authTokenService.CreateClaimsPrincipal(userId);
		}

		await next(context);
	}

	private static bool TryGetBearerToken(HttpRequest request, out string accessToken)
	{
		const string bearerPrefix = "Bearer ";
		var authorization = request.Headers.Authorization.ToString();
		if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
		{
			accessToken = authorization[bearerPrefix.Length..].Trim();
			return !string.IsNullOrWhiteSpace(accessToken);
		}

		accessToken = string.Empty;
		return false;
	}
}

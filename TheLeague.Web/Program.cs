using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using TheLeague.Interfaces;
using TheLeague.Mongo.Context;
using TheLeague.Mongo.Services;
using TheLeague.Web.Middleware;
using TheLeague.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services
	.AddControllers()
	.AddJsonOptions(options =>
	{
		options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
	});

builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));
builder.Services.AddSingleton<LeagueDataContext>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ILeagueAuthorisationService, LeagueAuthorisationService>();
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddScoped<ILeagueMemberService, LeagueMemberService>();
builder.Services.AddScoped<ILeaguePresetService, LeaguePresetService>();
builder.Services.AddScoped<IChallengeService, ChallengeService>();
builder.Services.AddScoped<IPointSubmissionService, PointSubmissionService>();
builder.Services.AddScoped<IPointAllocationService, PointAllocationService>();
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddSingleton<AuthTokenService>();

builder.Services
	.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
	.AddCookie(options =>
	{
		options.Cookie.Name = "theleague.auth";
		options.Cookie.HttpOnly = true;
		options.Cookie.SameSite = builder.Environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None;
		options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
		options.Events.OnRedirectToLogin = context =>
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return Task.CompletedTask;
		};
	});

builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
	var allowedOrigins = builder.Configuration
		.GetSection("Cors:AllowedOrigins")
		.Get<string[]>()
		?.Select(origin => origin.Trim().TrimEnd('/'))
		.Where(origin => !string.IsNullOrWhiteSpace(origin))
		.ToArray();

	if (allowedOrigins is null || allowedOrigins.Length == 0)
	{
		allowedOrigins =
		[
			"http://localhost:5173",
			"http://127.0.0.1:5173",
			"https://localhost:5173"
		];
	}

	options.AddPolicy("frontend", policy =>
	{
		policy
			.SetIsOriginAllowed(origin => IsAllowedFrontendOrigin(origin, allowedOrigins))
			.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials();
	});
});

var app = builder.Build();

app.UseExceptionHandler(exceptionApp =>
{
	exceptionApp.Run(async context =>
	{
		var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
		context.Response.ContentType = "application/json";
		context.Response.StatusCode = exception switch
		{
			UnauthorizedAccessException => StatusCodes.Status403Forbidden,
			InvalidOperationException => StatusCodes.Status400BadRequest,
			_ => StatusCodes.Status500InternalServerError
		};

		await context.Response.WriteAsJsonAsync(new { error = exception?.Message ?? "Unexpected error." });
	});
});

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseMiddleware<BearerTokenMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();

static bool IsAllowedFrontendOrigin(string origin, IReadOnlyCollection<string> allowedOrigins)
{
	if (allowedOrigins.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
	{
		return true;
	}

	if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
	{
		return false;
	}

	return uri.Scheme == Uri.UriSchemeHttps &&
		(uri.Host.Equals("vercel.app", StringComparison.OrdinalIgnoreCase) ||
		 uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase));
}

public partial class Program;

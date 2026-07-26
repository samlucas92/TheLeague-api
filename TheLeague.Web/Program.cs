using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;
using TheLeague.Mongo.Services;
using TheLeague.Web.Security;
using TheLeague.Web.Services;
using TheLeague.Web.WebModels;

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");

if (!string.IsNullOrWhiteSpace(port))
{
	builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var jwtSettings = builder.Configuration
	.GetSection("Jwt")
	.Get<JwtSettings>() ?? new JwtSettings();

jwtSettings.Secret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? jwtSettings.Secret;

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
{
	jwtSettings.Secret = "development-only-theleague-jwt-secret-change-this-before-production";
}

builder.Services.AddOpenApi();
builder.Services
	.AddControllers()
	.AddJsonOptions(options =>
	{
		options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
	});

builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<ResendSettings>(builder.Configuration.GetSection("Resend"));
builder.Services.Configure<JwtSettings>(options =>
{
	options.Issuer = jwtSettings.Issuer;
	options.Audience = jwtSettings.Audience;
	options.Secret = jwtSettings.Secret;
	options.ExpiryMinutes = jwtSettings.ExpiryMinutes;
});
builder.Services.AddSingleton<LeagueDataContext>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ILeagueAuthorisationService, LeagueAuthorisationService>();
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddScoped<ILeagueMemberService, LeagueMemberService>();
builder.Services.AddScoped<ILeaguePresetService, LeaguePresetService>();
builder.Services.AddScoped<IChallengeService, ChallengeService>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<IPointSubmissionService, PointSubmissionService>();
builder.Services.AddScoped<IPointAllocationService, PointAllocationService>();
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddScoped<ILeagueAuditService, LeagueAuditService>();
builder.Services.AddScoped<IEmailOutboxService, EmailOutboxService>();
builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));
builder.Services
	.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(options =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidateAudience = true,
			ValidateLifetime = true,
			ValidateIssuerSigningKey = true,
			ValidIssuer = jwtSettings.Issuer,
			ValidAudience = jwtSettings.Audience,
			IssuerSigningKey = signingKey,
			ClockSkew = TimeSpan.FromMinutes(2)
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
app.UseAuthorization();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", name = "The League" })).AllowAnonymous();
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

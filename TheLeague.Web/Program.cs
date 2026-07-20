using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;
using TheLeague.Mongo.Services;
using TheLeague.Web.Extensions;
using TheLeague.Web.WebModels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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
		?.Where(origin => !string.IsNullOrWhiteSpace(origin))
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
			.WithOrigins(allowedOrigins)
			.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials();
	});
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapPost("/auth/register", async (
	RegisterRequest request,
	IUserService userService,
	HttpContext httpContext,
	CancellationToken cancellationToken) =>
{
	var account = await userService.RegisterAsync(request.Name, request.EmailAddress, request.Password, cancellationToken);
	await SignInAsync(httpContext, account);
	return Results.Ok(ToAuthenticatedUser(account));
});

api.MapPost("/auth/login", async (
	LoginRequest request,
	IUserService userService,
	HttpContext httpContext,
	CancellationToken cancellationToken) =>
{
	var account = await userService.ValidateCredentialsAsync(request.EmailAddress, request.Password, cancellationToken);
	if (account is null)
	{
		return Results.Unauthorized();
	}

	await SignInAsync(httpContext, account);
	return Results.Ok(ToAuthenticatedUser(account));
});

api.MapPost("/auth/signout", async (HttpContext httpContext) =>
{
	await httpContext.SignOutAsync();
	return Results.NoContent();
}).RequireAuthorization();

api.MapGet("/auth/me", async (ClaimsPrincipal principal, IUserService userService, CancellationToken cancellationToken) =>
{
	var account = await userService.GetByIdAsync(principal.GetRequiredUserId(), cancellationToken);
	return account is null ? Results.Unauthorized() : Results.Ok(ToAuthenticatedUser(account));
}).RequireAuthorization();

api.MapPost("/auth/change-password", () => Results.StatusCode(StatusCodes.Status501NotImplemented)).RequireAuthorization();
api.MapPost("/auth/forgot-password", () => Results.Accepted());
api.MapPost("/auth/reset-password", () => Results.StatusCode(StatusCodes.Status501NotImplemented));

api.MapGet("/leagues", async (ClaimsPrincipal principal, ILeagueService leagueService, CancellationToken cancellationToken) =>
	Results.Ok(await leagueService.ListForUserAsync(principal.GetRequiredUserId(), cancellationToken))).RequireAuthorization();

api.MapPost("/leagues", async (
	CreateLeagueRequest request,
	ClaimsPrincipal principal,
	ILeagueService leagueService,
	CancellationToken cancellationToken) =>
{
	var league = await leagueService.CreateAsync(principal.GetRequiredUserId(), request.Name, request.Description, request.PresetType, request.JoinMode, cancellationToken);
	return Results.Created($"/api/leagues/{league.Id}", league);
}).RequireAuthorization();

api.MapGet("/leagues/{leagueId:guid}", async (
	Guid leagueId,
	ClaimsPrincipal principal,
	ILeagueService leagueService,
	ILeagueAuthorisationService authorisationService,
	CancellationToken cancellationToken) =>
{
	await authorisationService.GetRequiredMembershipAsync(leagueId, principal.GetRequiredUserId(), cancellationToken);
	var league = await leagueService.GetAsync(leagueId, cancellationToken);
	return league is null ? Results.NotFound() : Results.Ok(league);
}).RequireAuthorization();

api.MapPost("/leagues/join-preview", async (
	JoinPreviewRequest request,
	ILeagueService leagueService,
	CancellationToken cancellationToken) => Results.Ok(await leagueService.PreviewJoinAsync(request.JoinCode, cancellationToken))).RequireAuthorization();

api.MapGet("/public/leagues/{joinCode}", async (
	string joinCode,
	ILeagueService leagueService,
	CancellationToken cancellationToken) => Results.Ok(await leagueService.GetPublicViewAsync(joinCode, cancellationToken)));

api.MapPost("/leagues/join", async (
	JoinLeagueRequest request,
	ClaimsPrincipal principal,
	ILeagueMemberService memberService,
	CancellationToken cancellationToken) => Results.Ok(await memberService.JoinAsync(principal.GetRequiredUserId(), request.JoinCode, request.DisplayName, cancellationToken))).RequireAuthorization();

api.MapGet("/leagues/{leagueId:guid}/members", async (
	Guid leagueId,
	ClaimsPrincipal principal,
	ILeagueMemberService memberService,
	CancellationToken cancellationToken) => Results.Ok(await memberService.ListAsync(leagueId, principal.GetRequiredUserId(), cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/members/offline", async (
	Guid leagueId,
	AddOfflineMemberRequest request,
	ClaimsPrincipal principal,
	ILeagueMemberService memberService,
	CancellationToken cancellationToken) => Results.Created($"/api/leagues/{leagueId}/members", await memberService.AddOfflineMemberAsync(leagueId, principal.GetRequiredUserId(), request.DisplayName, request.EmailAddress, request.Role, cancellationToken))).RequireAuthorization();

api.MapPut("/leagues/{leagueId:guid}/members/{memberId:guid}/role", async (
	Guid leagueId,
	Guid memberId,
	ChangeMemberRoleRequest request,
	ClaimsPrincipal principal,
	ILeagueMemberService memberService,
	CancellationToken cancellationToken) => Results.Ok(await memberService.ChangeRoleAsync(leagueId, principal.GetRequiredUserId(), memberId, request.Role, cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/members/{memberId:guid}/link", async (
	Guid leagueId,
	Guid memberId,
	LinkOfflineMemberRequest request,
	ClaimsPrincipal principal,
	ILeagueMemberService memberService,
	CancellationToken cancellationToken) => Results.Ok(await memberService.LinkOfflineMemberAsync(leagueId, principal.GetRequiredUserId(), memberId, request.EmailAddress, cancellationToken))).RequireAuthorization();

api.MapGet("/leagues/{leagueId:guid}/challenges", async (
	Guid leagueId,
	ClaimsPrincipal principal,
	IChallengeService challengeService,
	CancellationToken cancellationToken) => Results.Ok(await challengeService.ListAsync(leagueId, principal.GetRequiredUserId(), cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/challenges", async (
	Guid leagueId,
	CreateChallengeRequest request,
	ClaimsPrincipal principal,
	IChallengeService challengeService,
	CancellationToken cancellationToken) =>
{
	var challenge = new Challenge
	{
		Name = request.Name,
		Description = request.Description,
		TargetMemberIds = request.TargetMemberIds?.ToList() ?? [],
		PointsForSuccess = request.PointsForSuccess ?? request.FixedPoints ?? 0,
		PointsForFailure = request.PointsForFailure ?? 0,
		ScoringType = request.ScoringType ?? ChallengeScoringType.Fixed,
		FixedPoints = request.FixedPoints,
		MinimumPoints = request.MinimumPoints,
		MaximumPoints = request.MaximumPoints,
		RepeatType = request.RepeatType,
		SubmissionLimit = request.SubmissionLimit,
		RequiresEvidence = request.RequiresEvidence,
		IsSecret = request.IsSecret
	};

	return Results.Created($"/api/leagues/{leagueId}/challenges", await challengeService.CreateAsync(leagueId, principal.GetRequiredUserId(), challenge, cancellationToken));
}).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/challenges/{challengeId:guid}/accept", async (
	Guid leagueId,
	Guid challengeId,
	ClaimsPrincipal principal,
	IChallengeService challengeService,
	CancellationToken cancellationToken) => Results.Ok(await challengeService.AcceptAsync(leagueId, principal.GetRequiredUserId(), challengeId, cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/challenges/{challengeId:guid}/reject", async (
	Guid leagueId,
	Guid challengeId,
	ClaimsPrincipal principal,
	IChallengeService challengeService,
	CancellationToken cancellationToken) => Results.Ok(await challengeService.RejectAsync(leagueId, principal.GetRequiredUserId(), challengeId, cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/challenges/{challengeId:guid}/complete", async (
	Guid leagueId,
	Guid challengeId,
	ClaimsPrincipal principal,
	IChallengeService challengeService,
	CancellationToken cancellationToken) => Results.Ok(await challengeService.CompleteAsync(leagueId, principal.GetRequiredUserId(), challengeId, cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/challenges/{challengeId:guid}/fail", async (
	Guid leagueId,
	Guid challengeId,
	ClaimsPrincipal principal,
	IChallengeService challengeService,
	CancellationToken cancellationToken) => Results.Ok(await challengeService.FailAsync(leagueId, principal.GetRequiredUserId(), challengeId, cancellationToken))).RequireAuthorization();

api.MapGet("/leagues/{leagueId:guid}/submissions/mine", async (
	Guid leagueId,
	ClaimsPrincipal principal,
	IPointSubmissionService submissionService,
	CancellationToken cancellationToken) => Results.Ok(await submissionService.ListMineAsync(leagueId, principal.GetRequiredUserId(), cancellationToken))).RequireAuthorization();

api.MapGet("/leagues/{leagueId:guid}/submissions/pending", async (
	Guid leagueId,
	ClaimsPrincipal principal,
	IPointSubmissionService submissionService,
	CancellationToken cancellationToken) => Results.Ok(await submissionService.ListPendingAsync(leagueId, principal.GetRequiredUserId(), cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/submissions", async (
	Guid leagueId,
	CreateSubmissionRequest request,
	ClaimsPrincipal principal,
	IPointSubmissionService submissionService,
	CancellationToken cancellationToken) => Results.Created($"/api/leagues/{leagueId}/submissions", await submissionService.CreateAsync(leagueId, principal.GetRequiredUserId(), request.ChallengeId, request.LeagueMemberId, request.RequestedPoints, request.PublicReason, cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/submissions/{submissionId:guid}/approve", async (
	Guid leagueId,
	Guid submissionId,
	ApproveSubmissionRequest request,
	ClaimsPrincipal principal,
	IPointSubmissionService submissionService,
	CancellationToken cancellationToken) => Results.Ok(await submissionService.ApproveAsync(leagueId, principal.GetRequiredUserId(), submissionId, request.ApprovedPoints, request.PublicReviewReason, request.AdminReviewNote, cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/submissions/{submissionId:guid}/reject", async (
	Guid leagueId,
	Guid submissionId,
	RejectSubmissionRequest request,
	ClaimsPrincipal principal,
	IPointSubmissionService submissionService,
	CancellationToken cancellationToken) => Results.Ok(await submissionService.RejectAsync(leagueId, principal.GetRequiredUserId(), submissionId, request.PublicReviewReason, request.AdminReviewNote, cancellationToken))).RequireAuthorization();

api.MapGet("/leagues/{leagueId:guid}/leaderboard", async (
	Guid leagueId,
	ClaimsPrincipal principal,
	ILeaderboardService leaderboardService,
	CancellationToken cancellationToken) => Results.Ok(await leaderboardService.GetAsync(leagueId, principal.GetRequiredUserId(), cancellationToken))).RequireAuthorization();

api.MapGet("/leagues/{leagueId:guid}/points-feed", async (
	Guid leagueId,
	ClaimsPrincipal principal,
	IPointAllocationService allocationService,
	CancellationToken cancellationToken) => Results.Ok(await allocationService.ListFeedAsync(leagueId, principal.GetRequiredUserId(), cancellationToken))).RequireAuthorization();

api.MapPost("/leagues/{leagueId:guid}/allocations", async (
	Guid leagueId,
	CreateManualAllocationRequest request,
	ClaimsPrincipal principal,
	IPointAllocationService allocationService,
	CancellationToken cancellationToken) => Results.Created($"/api/leagues/{leagueId}/points-feed", await allocationService.CreateManualAsync(leagueId, principal.GetRequiredUserId(), request.LeagueMemberId, request.Points, request.Reason, cancellationToken))).RequireAuthorization();

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

app.Run();

static AuthenticatedUser ToAuthenticatedUser(UserAccount account) => new(account.Id, account.Name, account.EmailAddress);

static Task SignInAsync(HttpContext httpContext, UserAccount account)
{
	var claims = new[]
	{
		new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
		new Claim(ClaimTypes.Name, account.Name),
		new Claim(ClaimTypes.Email, account.EmailAddress)
	};
	var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
	return httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
}

public partial class Program;

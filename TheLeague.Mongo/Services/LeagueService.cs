using System.Security.Cryptography;
using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class LeagueService(
	LeagueDataContext context,
	ILeagueMemberService memberService,
	ILeaguePresetService presetService,
	ILeaderboardService leaderboardService,
	IPointAllocationService allocationService,
	IChallengeService challengeService,
	ILeagueAuthorisationService authorisationService) : ILeagueService
{
	public async Task<League> CreateAsync(Guid ownerUserId, string name, string? description, LeaguePresetType presetType, LeagueJoinMode joinMode, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new InvalidOperationException("League name is required.");
		}

		var league = new League
		{
			Id = Guid.NewGuid(),
			Name = name.Trim(),
			Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
			OwnerUserId = ownerUserId,
			JoinCode = GenerateUniqueJoinCode(),
			Status = LeagueStatus.Active,
			JoinMode = joinMode,
			PresetType = presetType,
			PublicViewEnabled = true,
			AllowMembersToLeave = true,
			ShowPendingPointsOnLeaderboard = true,
			CreatedAt = DateTime.UtcNow
		};

		if (!context.Users.TryGetValue(ownerUserId, out var owner))
		{
			throw new UnauthorizedAccessException("Please sign in again.");
		}

		context.Leagues[league.Id] = league;
		await memberService.CreateOwnerAsync(league.Id, ownerUserId, owner.Name, cancellationToken);
		await presetService.ApplyPresetAsync(league.Id, presetType, cancellationToken);

		return league;
	}

	public Task<IReadOnlyCollection<LeagueSummary>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var summaries = context.Members.Values
			.Where(member => member.UserId == userId && member.Status is LeagueMemberStatus.Active or LeagueMemberStatus.Pending)
			.Join(context.Leagues.Values, member => member.LeagueId, league => league.Id, (member, league) =>
				new LeagueSummary(league.Id, league.Name, league.Description, league.Status, member.Role, member.Status, league.JoinCode))
			.OrderBy(summary => summary.Name)
			.ToArray();

		return Task.FromResult<IReadOnlyCollection<LeagueSummary>>(summaries);
	}

	public Task<League?> GetAsync(Guid leagueId, CancellationToken cancellationToken = default)
	{
		context.Leagues.TryGetValue(leagueId, out var league);
		return Task.FromResult(league);
	}

	public async Task<League> UpdateSettingsAsync(Guid leagueId, Guid userId, string name, string? description, LeagueJoinMode joinMode, bool publicViewEnabled, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var league = GetLeague(leagueId);
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new InvalidOperationException("League name is required.");
		}

		league.Name = name.Trim();
		league.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
		league.JoinMode = joinMode;
		league.PublicViewEnabled = publicViewEnabled;
		league.UpdatedAt = DateTime.UtcNow;
		await context.Leagues.SaveAsync(league, cancellationToken);
		return league;
	}

	public async Task<League> RegenerateJoinCodeAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var league = GetLeague(leagueId);
		league.JoinCode = GenerateUniqueJoinCode();
		league.UpdatedAt = DateTime.UtcNow;
		await context.Leagues.SaveAsync(league, cancellationToken);
		return league;
	}

	public Task<JoinPreview> PreviewJoinAsync(string joinCode, CancellationToken cancellationToken = default)
	{
		var normalized = NormalizeJoinCode(joinCode);
		var league = context.Leagues.Values
			.Where(league => league.JoinCode == normalized && league.Status == LeagueStatus.Active)
			.OrderByDescending(league => league.CreatedAt)
			.FirstOrDefault();
		if (league is null)
		{
			throw new InvalidOperationException("Join code was not found.");
		}

		if (!context.Users.TryGetValue(league.OwnerUserId, out var owner))
		{
			throw new InvalidOperationException("League owner was not found.");
		}

		var preview = new JoinPreview(league.Id, league.Name, league.Description, owner.Name, league.JoinMode, league.JoinMode == LeagueJoinMode.ApprovalRequired);
		return Task.FromResult(preview);
	}

	public async Task<PublicLeagueView> GetPublicViewAsync(string joinCode, CancellationToken cancellationToken = default)
	{
		var normalized = NormalizeJoinCode(joinCode);
		var league = context.Leagues.Values
			.Where(league => league.JoinCode == normalized && league.Status == LeagueStatus.Active)
			.OrderByDescending(league => league.CreatedAt)
			.FirstOrDefault();
		if (league is null)
		{
			throw new InvalidOperationException("Join code was not found.");
		}

		if (!league.PublicViewEnabled)
		{
			throw new InvalidOperationException("Public view is disabled for this league.");
		}

		var members = context.Members.Values
			.Where(member => member.LeagueId == league.Id && member.Status == LeagueMemberStatus.Active)
			.OrderBy(member => member.DisplayName)
			.ToArray();
		var leaderboard = await leaderboardService.GetPublicAsync(league.Id, cancellationToken);
		var feed = await allocationService.ListPublicFeedAsync(league.Id, cancellationToken);
		var challenges = await challengeService.ListPublicAsync(league.Id, cancellationToken);

		return new PublicLeagueView(league, members, leaderboard, feed, challenges);
	}

	private League GetLeague(Guid leagueId)
	{
		if (!context.Leagues.TryGetValue(leagueId, out var league))
		{
			throw new InvalidOperationException("League was not found.");
		}

		return league;
	}

	internal static string NormalizeJoinCode(string joinCode)
	{
		if (string.IsNullOrWhiteSpace(joinCode))
		{
			throw new InvalidOperationException("Join code is required.");
		}

		return new string(joinCode.Trim().Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
	}

	private string GenerateUniqueJoinCode()
	{
		const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
		Span<byte> bytes = stackalloc byte[6];

		for (var attempt = 0; attempt < 100; attempt++)
		{
			RandomNumberGenerator.Fill(bytes);
			var code = new string(bytes.ToArray().Select(value => alphabet[value % alphabet.Length]).ToArray());
			if (!context.Leagues.Values.Any(league => league.JoinCode == code && league.Status == LeagueStatus.Active))
			{
				return code;
			}
		}

		throw new InvalidOperationException("Could not generate a unique join code.");
	}
}

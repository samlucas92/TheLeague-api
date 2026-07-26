using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class TournamentService(LeagueDataContext context, ILeagueAuthorisationService authorisationService, ILeagueAuditService auditService) : ITournamentService
{
	public async Task<IReadOnlyCollection<TournamentListItem>> ListAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		return await ListPublicAsync(leagueId, cancellationToken);
	}

	public Task<IReadOnlyCollection<TournamentListItem>> ListPublicAsync(Guid leagueId, CancellationToken cancellationToken = default)
	{
		var tournaments = context.Tournaments.Values
			.Where(tournament => tournament.LeagueId == leagueId)
			.OrderByDescending(tournament => tournament.Status == TournamentStatus.Active)
			.ThenByDescending(tournament => tournament.CreatedAt)
			.Select(ToListItem)
			.ToArray();

		return Task.FromResult<IReadOnlyCollection<TournamentListItem>>(tournaments);
	}

	public async Task<TournamentListItem> CreateAsync(Guid leagueId, Guid userId, Tournament tournament, IReadOnlyCollection<Guid> participantMemberIds, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);
		var participants = ValidateParticipants(leagueId, participantMemberIds);
		ValidateTournament(tournament, participants.Count);

		tournament.Id = Guid.NewGuid();
		tournament.LeagueId = leagueId;
		tournament.CreatedByUserId = userId;
		tournament.Name = tournament.Name.Trim();
		tournament.Status = TournamentStatus.Active;
		tournament.Participants = participants;
		tournament.EliminatePerRound = Math.Max(1, tournament.EliminatePerRound);
		tournament.CreatedAt = DateTime.UtcNow;

		if (tournament.Format == TournamentFormat.SingleEliminationBracket)
		{
			tournament.Matches = BuildOpeningBracket(participants);
		}
		else
		{
			tournament.Rounds = [BuildNextRound(participants, 1)];
		}

		context.Tournaments[tournament.Id] = tournament;
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.TournamentCreated, "Tournament", tournament.Id, $"Created tournament {tournament.Name}.", cancellationToken);
		return ToListItem(tournament);
	}

	public async Task<TournamentListItem> CompleteMatchAsync(Guid leagueId, Guid userId, Guid tournamentId, Guid matchId, Guid winnerMemberId, int? playerOneScore, int? playerTwoScore, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var tournament = GetTournament(leagueId, tournamentId);
		if (tournament.Format != TournamentFormat.SingleEliminationBracket)
		{
			throw new InvalidOperationException("This tournament does not use matches.");
		}

		var match = tournament.Matches.SingleOrDefault(candidate => candidate.Id == matchId)
			?? throw new InvalidOperationException("Tournament match was not found.");
		if (match.Status == TournamentMatchStatus.Completed)
		{
			return ToListItem(tournament);
		}

		var matchPlayers = new[] { match.PlayerOneMemberId, match.PlayerTwoMemberId }.Where(id => id.HasValue).Select(id => id!.Value).ToArray();
		if (!matchPlayers.Contains(winnerMemberId))
		{
			throw new InvalidOperationException("Winner must be one of the match players.");
		}

		match.PlayerOneScore = playerOneScore;
		match.PlayerTwoScore = playerTwoScore;
		match.WinnerMemberId = winnerMemberId;
		match.Status = TournamentMatchStatus.Completed;
		match.CompletedAt = DateTime.UtcNow;
		AwardPointsOnce(tournament, winnerMemberId, tournament.MatchWinPoints, $"Won tournament match: {tournament.Name}", userId);
		AdvanceBracket(tournament, match, userId);
		tournament.UpdatedAt = DateTime.UtcNow;
		await context.Tournaments.SaveAsync(tournament, cancellationToken);
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.TournamentMatchCompleted, "Tournament", tournament.Id, $"Completed a match in {tournament.Name}.", cancellationToken);
		return ToListItem(tournament);
	}

	public async Task<TournamentListItem> ScoreRoundAsync(Guid leagueId, Guid userId, Guid tournamentId, IReadOnlyDictionary<Guid, int> scores, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var tournament = GetTournament(leagueId, tournamentId);
		if (tournament.Format != TournamentFormat.RoundElimination)
		{
			throw new InvalidOperationException("This tournament does not use round scoring.");
		}

		var activeParticipants = tournament.Participants.Where(participant => !participant.IsEliminated).ToArray();
		var round = tournament.Rounds.LastOrDefault(round => !round.IsComplete) ?? BuildNextRound(activeParticipants, tournament.Rounds.Count + 1);
		foreach (var participant in activeParticipants)
		{
			if (!scores.TryGetValue(participant.LeagueMemberId, out var score))
			{
				throw new InvalidOperationException("Every active player needs a score.");
			}

			var roundScore = round.Scores.Single(entry => entry.LeagueMemberId == participant.LeagueMemberId);
			roundScore.Score = score;
			participant.TotalScore += score;
		}

		var ordered = round.Scores
			.Where(entry => entry.Score.HasValue)
			.OrderByDescending(entry => entry.Score!.Value)
			.ToArray();
		round.RoundWinnerMemberId = ordered.First().LeagueMemberId;
		var remainingAfterThisRound = activeParticipants.Length - Math.Min(tournament.EliminatePerRound, activeParticipants.Length - 1);
		round.EliminatedMemberIds = ordered.Skip(remainingAfterThisRound).Select(entry => entry.LeagueMemberId).ToList();
		foreach (var eliminatedId in round.EliminatedMemberIds)
		{
			tournament.Participants.Single(participant => participant.LeagueMemberId == eliminatedId).IsEliminated = true;
		}

		round.IsComplete = true;
		round.CompletedAt = DateTime.UtcNow;
		if (!tournament.Rounds.Any(existing => existing.RoundNumber == round.RoundNumber))
		{
			tournament.Rounds.Add(round);
		}

		var remaining = tournament.Participants.Where(participant => !participant.IsEliminated).ToArray();
		if (remaining.Length == 1)
		{
			CompleteTournament(tournament, remaining[0].LeagueMemberId, userId);
		}
		else
		{
			tournament.Rounds.Add(BuildNextRound(remaining, tournament.Rounds.Count + 1));
		}

		tournament.UpdatedAt = DateTime.UtcNow;
		await context.Tournaments.SaveAsync(tournament, cancellationToken);
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.TournamentRoundScored, "Tournament", tournament.Id, $"Scored round {round.RoundNumber} in {tournament.Name}.", cancellationToken);
		return ToListItem(tournament);
	}

	public async Task DeleteAsync(Guid leagueId, Guid userId, Guid tournamentId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var tournament = GetTournament(leagueId, tournamentId);
		await context.Tournaments.RemoveAsync(tournamentId, cancellationToken);
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.TournamentDeleted, "Tournament", tournamentId, $"Removed tournament {tournament.Name}.", cancellationToken);
	}

	private Tournament GetTournament(Guid leagueId, Guid tournamentId)
	{
		if (!context.Tournaments.TryGetValue(tournamentId, out var tournament) || tournament.LeagueId != leagueId)
		{
			throw new InvalidOperationException("Tournament was not found.");
		}

		return tournament;
	}

	private List<TournamentParticipant> ValidateParticipants(Guid leagueId, IReadOnlyCollection<Guid> participantMemberIds)
	{
		var distinctIds = participantMemberIds.Distinct().ToArray();
		if (distinctIds.Length < 2)
		{
			throw new InvalidOperationException("Choose at least two tournament players.");
		}

		return distinctIds.Select((memberId, index) =>
		{
			if (!context.Members.TryGetValue(memberId, out var member) || member.LeagueId != leagueId || member.Status != LeagueMemberStatus.Active)
			{
				throw new InvalidOperationException("A tournament player was not found.");
			}

			return new TournamentParticipant
			{
				LeagueMemberId = memberId,
				DisplayName = member.DisplayName,
				Seed = index + 1
			};
		}).ToList();
	}

	private static void ValidateTournament(Tournament tournament, int participantCount)
	{
		if (string.IsNullOrWhiteSpace(tournament.Name))
		{
			throw new InvalidOperationException("Tournament name is required.");
		}

		if ((tournament.GameType is TournamentGameType.Pool or TournamentGameType.Darts301 or TournamentGameType.Darts501) && tournament.Format != TournamentFormat.SingleEliminationBracket)
		{
			throw new InvalidOperationException("This tournament type currently uses knockout brackets.");
		}

		if (tournament.GameType == TournamentGameType.DartsHighestScore && tournament.Format != TournamentFormat.RoundElimination)
		{
			throw new InvalidOperationException("Highest-score darts tournaments use round elimination.");
		}

		if (tournament.EliminatePerRound >= participantCount && tournament.Format == TournamentFormat.RoundElimination)
		{
			throw new InvalidOperationException("The elimination count must leave at least one player each round.");
		}
	}

	private static List<TournamentMatch> BuildOpeningBracket(IReadOnlyList<TournamentParticipant> participants)
	{
		var matches = new List<TournamentMatch>();
		var matchNumber = 1;
		for (var i = 0; i < participants.Count; i += 2)
		{
			var playerOne = participants[i].LeagueMemberId;
			var playerTwo = i + 1 < participants.Count ? participants[i + 1].LeagueMemberId : (Guid?)null;
			matches.Add(new TournamentMatch
			{
				Id = Guid.NewGuid(),
				RoundNumber = 1,
				MatchNumber = matchNumber++,
				PlayerOneMemberId = playerOne,
				PlayerTwoMemberId = playerTwo,
				WinnerMemberId = playerTwo is null ? playerOne : null,
				Status = playerTwo is null ? TournamentMatchStatus.Completed : TournamentMatchStatus.Ready,
				CompletedAt = playerTwo is null ? DateTime.UtcNow : null
			});
		}

		return matches;
	}

	private static TournamentRound BuildNextRound(IEnumerable<TournamentParticipant> participants, int roundNumber) => new()
	{
		RoundNumber = roundNumber,
		Scores = participants.Select(participant => new TournamentRoundScore { LeagueMemberId = participant.LeagueMemberId }).ToList()
	};

	private void AdvanceBracket(Tournament tournament, TournamentMatch completedMatch, Guid userId)
	{
		var currentRoundMatches = tournament.Matches.Where(match => match.RoundNumber == completedMatch.RoundNumber).ToArray();
		if (currentRoundMatches.Any(match => match.Status != TournamentMatchStatus.Completed))
		{
			return;
		}

		var winners = currentRoundMatches.Select(match => match.WinnerMemberId!.Value).ToArray();
		if (winners.Length == 1)
		{
			CompleteTournament(tournament, winners[0], userId);
			var runnerUpId = currentRoundMatches.Single().PlayerOneMemberId == winners[0]
				? currentRoundMatches.Single().PlayerTwoMemberId
				: currentRoundMatches.Single().PlayerOneMemberId;
			if (runnerUpId.HasValue)
			{
				AwardPointsOnce(tournament, runnerUpId.Value, tournament.RunnerUpPoints, $"Runner up in tournament: {tournament.Name}", userId);
			}
			return;
		}

		var nextRoundNumber = completedMatch.RoundNumber + 1;
		if (tournament.Matches.Any(match => match.RoundNumber == nextRoundNumber))
		{
			return;
		}

		var matchNumber = 1;
		for (var i = 0; i < winners.Length; i += 2)
		{
			var playerOne = winners[i];
			var playerTwo = i + 1 < winners.Length ? winners[i + 1] : (Guid?)null;
			tournament.Matches.Add(new TournamentMatch
			{
				Id = Guid.NewGuid(),
				RoundNumber = nextRoundNumber,
				MatchNumber = matchNumber++,
				PlayerOneMemberId = playerOne,
				PlayerTwoMemberId = playerTwo,
				WinnerMemberId = playerTwo is null ? playerOne : null,
				Status = playerTwo is null ? TournamentMatchStatus.Completed : TournamentMatchStatus.Ready,
				CompletedAt = playerTwo is null ? DateTime.UtcNow : null
			});
		}
	}

	private void CompleteTournament(Tournament tournament, Guid winnerMemberId, Guid userId)
	{
		tournament.WinnerMemberId = winnerMemberId;
		tournament.Status = TournamentStatus.Completed;
		tournament.CompletedAt = DateTime.UtcNow;
		AwardPointsOnce(tournament, winnerMemberId, tournament.WinnerPoints, $"Won tournament: {tournament.Name}", userId);
	}

	private void AwardPointsOnce(Tournament tournament, Guid memberId, int points, string reason, Guid userId)
	{
		if (points == 0)
		{
			return;
		}

		var alreadyAwarded = context.Allocations.Values.Any(allocation =>
			allocation.LeagueId == tournament.LeagueId &&
			allocation.LeagueMemberId == memberId &&
			allocation.Source == PointAllocationSource.TournamentAward &&
			allocation.Reason == reason);
		if (alreadyAwarded)
		{
			return;
		}

		var allocation = new PointAllocation
		{
			Id = Guid.NewGuid(),
			LeagueId = tournament.LeagueId,
			LeagueMemberId = memberId,
			ChallengeId = tournament.ChallengeId,
			Points = points,
			Reason = reason,
			Source = PointAllocationSource.TournamentAward,
			AwardedByUserId = userId,
			AwardedAt = DateTime.UtcNow
		};
		context.Allocations[allocation.Id] = allocation;
	}

	private TournamentListItem ToListItem(Tournament tournament)
	{
		var winnerName = tournament.WinnerMemberId.HasValue
			? tournament.Participants.SingleOrDefault(participant => participant.LeagueMemberId == tournament.WinnerMemberId)?.DisplayName
			: null;

		return new TournamentListItem(
			tournament.Id,
			tournament.Name,
			tournament.GameType,
			tournament.Format,
			tournament.Status,
			tournament.ChallengeId,
			tournament.Participants.Count,
			tournament.WinnerPoints,
			tournament.RunnerUpPoints,
			tournament.MatchWinPoints,
			tournament.EliminatePerRound,
			tournament.WinnerMemberId,
			winnerName,
			tournament.CreatedAt,
			tournament.CompletedAt,
			tournament.Participants,
			tournament.Matches,
			tournament.Rounds);
	}
}

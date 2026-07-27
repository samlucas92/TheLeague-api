using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;
using System.Security.Cryptography;

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
		tournament.Participants = ShuffleParticipants(participants);
		tournament.FramesOrLegs = Math.Max(1, tournament.FramesOrLegs);
		tournament.GroupSize = Math.Max(2, tournament.GroupSize);
		tournament.QualifiersPerGroup = Math.Clamp(tournament.QualifiersPerGroup, 1, tournament.GroupSize);
		tournament.RoundRules = tournament.RoundRules
			.Where(rule => rule.RoundNumber != 0 && rule.FramesOrLegs > 0)
			.GroupBy(rule => rule.RoundNumber)
			.Select(group => group.Last())
			.OrderBy(rule => rule.RoundNumber)
			.ToList();
		tournament.MinimumPlayers = Math.Max(2, tournament.MinimumPlayers);
		tournament.EliminatePerRound = Math.Max(1, tournament.EliminatePerRound);
		tournament.StartScore ??= tournament.GameType switch
		{
			TournamentGameType.Darts301 => 301,
			TournamentGameType.Darts501 => 501,
			_ => null
		};
		tournament.CreatedAt = DateTime.UtcNow;

		if (tournament.GameType == TournamentGameType.PubGolf)
		{
			tournament.Format = TournamentFormat.PubGolfCourse;
			tournament.HolesOrDefault();
			tournament.PubGolfScores = tournament.Participants
				.SelectMany(participant => tournament.PubGolfHoles.Select(hole => new PubGolfScore
				{
					LeagueMemberId = participant.LeagueMemberId,
					HoleId = hole.Id
				}))
				.ToList();
		}
		else if (tournament.Format == TournamentFormat.SingleEliminationBracket)
		{
			tournament.Matches = tournament.Structure == TournamentStructure.LeagueAndKnockout
				? BuildLeagueStage(tournament, tournament.Participants)
				: BuildOpeningBracket(tournament, tournament.Participants);
		}
		else
		{
			tournament.Rounds = [BuildNextRound(tournament.Participants, 1)];
		}

		context.Tournaments[tournament.Id] = tournament;
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.TournamentCreated, "Tournament", tournament.Id, $"Created tournament {tournament.Name}.", cancellationToken);
		return ToListItem(tournament);
	}

	public async Task<TournamentListItem> ScorePubGolfHoleAsync(Guid leagueId, Guid userId, Guid tournamentId, Guid holeId, IReadOnlyDictionary<Guid, int?> scores, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var tournament = GetTournament(leagueId, tournamentId);
		if (tournament.GameType != TournamentGameType.PubGolf)
		{
			throw new InvalidOperationException("This tournament does not use pub golf scorecards.");
		}

		var hole = tournament.PubGolfHoles.SingleOrDefault(candidate => candidate.Id == holeId)
			?? throw new InvalidOperationException("Pub golf hole was not found.");

		foreach (var participant in tournament.Participants)
		{
			if (!scores.TryGetValue(participant.LeagueMemberId, out var score))
			{
				continue;
			}

			if (score is < 1 or > 20)
			{
				throw new InvalidOperationException("Pub golf scores must be between 1 and 20.");
			}

			var scorecardEntry = tournament.PubGolfScores.Single(entry => entry.LeagueMemberId == participant.LeagueMemberId && entry.HoleId == hole.Id);
			scorecardEntry.Score = score;
		}

		UpdatePubGolfTotals(tournament, userId);
		tournament.UpdatedAt = DateTime.UtcNow;
		await context.Tournaments.SaveAsync(tournament, cancellationToken);
		await auditService.RecordAsync(leagueId, userId, LeagueAuditAction.TournamentRoundScored, "Tournament", tournament.Id, $"Scored hole {hole.HoleNumber} in {tournament.Name}.", cancellationToken);
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

		if (tournament.GameType == TournamentGameType.PubGolf)
		{
			if (tournament.Format != TournamentFormat.PubGolfCourse)
			{
				throw new InvalidOperationException("Pub golf tournaments use course scorecards.");
			}

			if (tournament.PubGolfHoles.Count == 0)
			{
				throw new InvalidOperationException("Pub golf needs at least one hole.");
			}

			if (tournament.PubGolfHoles.Any(hole => string.IsNullOrWhiteSpace(hole.Venue) || string.IsNullOrWhiteSpace(hole.Drink) || hole.Par < 1))
			{
				throw new InvalidOperationException("Each pub golf hole needs a venue, drink and par.");
			}
		}

		if (tournament.EliminatePerRound >= participantCount && tournament.Format == TournamentFormat.RoundElimination)
		{
			throw new InvalidOperationException("The elimination count must leave at least one player each round.");
		}

		if (tournament.FramesOrLegs < 1)
		{
			throw new InvalidOperationException("Match length must be at least one.");
		}
	}

	private static List<TournamentParticipant> ShuffleParticipants(IReadOnlyList<TournamentParticipant> participants) =>
		participants
			.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue))
			.Select((participant, index) => new TournamentParticipant
			{
				LeagueMemberId = participant.LeagueMemberId,
				DisplayName = participant.DisplayName,
				Seed = index + 1,
				IsEliminated = participant.IsEliminated,
				TotalScore = participant.TotalScore
			})
			.ToList();

	private static List<TournamentMatch> BuildOpeningBracket(Tournament tournament, IReadOnlyList<TournamentParticipant> participants)
	{
		var matches = new List<TournamentMatch>();
		var matchNumber = 1;
		var roundMatchCount = (int)Math.Ceiling(participants.Count / 2m);
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
				FramesOrLegs = GetFramesOrLegsForRound(tournament, 1, roundMatchCount),
				WinnerMemberId = playerTwo is null ? playerOne : null,
				Status = playerTwo is null ? TournamentMatchStatus.Completed : TournamentMatchStatus.Ready,
				CompletedAt = playerTwo is null ? DateTime.UtcNow : null
			});
		}

		return matches;
	}

	private static List<TournamentMatch> BuildLeagueStage(Tournament tournament, IReadOnlyList<TournamentParticipant> participants)
	{
		var matches = new List<TournamentMatch>();
		var matchNumber = 1;
		var groups = BuildBalancedGroups(participants, tournament.GroupSize);
		var groupFixtures = groups
			.Select((group, groupIndex) => new
			{
				GroupName = $"Group {(char)('A' + groupIndex)}",
				Rounds = BuildRoundRobinRounds(group)
			})
			.ToArray();
		var maxFixtureRounds = groupFixtures.Max(group => group.Rounds.Count);

		for (var fixtureRoundIndex = 0; fixtureRoundIndex < maxFixtureRounds; fixtureRoundIndex++)
		{
			foreach (var groupFixture in groupFixtures)
			{
				if (fixtureRoundIndex >= groupFixture.Rounds.Count)
				{
					continue;
				}

				foreach (var (playerOne, playerTwo) in groupFixture.Rounds[fixtureRoundIndex])
				{
					matches.Add(new TournamentMatch
					{
						Id = Guid.NewGuid(),
						RoundNumber = 0,
						MatchNumber = matchNumber++,
						GroupName = groupFixture.GroupName,
						PlayerOneMemberId = playerOne.LeagueMemberId,
						PlayerTwoMemberId = playerTwo.LeagueMemberId,
						FramesOrLegs = tournament.FramesOrLegs,
						Status = TournamentMatchStatus.Ready
					});
				}
			}
		}

		return matches;
	}

	private static List<TournamentParticipant[]> BuildBalancedGroups(IReadOnlyList<TournamentParticipant> participants, int groupSize)
	{
		var groupCount = Math.Max(1, (int)Math.Ceiling(participants.Count / (decimal)Math.Max(2, groupSize)));
		var groups = Enumerable.Range(0, groupCount).Select(_ => new List<TournamentParticipant>()).ToArray();
		for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
		{
			// Seed players into balanced groups. The forward/backward pass spreads stronger seeds
			// and avoids one undersized final group after randomisation.
			var group = groups[groupIndex];
			for (var participantIndex = groupIndex; participantIndex < participants.Count; participantIndex += groupCount * 2)
			{
				group.Add(participants[participantIndex]);
			}

			for (var participantIndex = (groupCount * 2) - groupIndex - 1; participantIndex < participants.Count; participantIndex += groupCount * 2)
			{
				group.Add(participants[participantIndex]);
			}
		}

		return groups.Select(group => group.ToArray()).ToList();
	}

	private static List<List<(TournamentParticipant PlayerOne, TournamentParticipant PlayerTwo)>> BuildRoundRobinRounds(IReadOnlyList<TournamentParticipant> group)
	{
		var players = group.Select<TournamentParticipant, TournamentParticipant?>(participant => participant).ToList();
		if (players.Count % 2 == 1)
		{
			players.Add(null);
		}

		var rounds = new List<List<(TournamentParticipant PlayerOne, TournamentParticipant PlayerTwo)>>();
		var roundCount = players.Count - 1;
		var half = players.Count / 2;
		for (var roundIndex = 0; roundIndex < roundCount; roundIndex++)
		{
			var round = new List<(TournamentParticipant PlayerOne, TournamentParticipant PlayerTwo)>();
			for (var pairIndex = 0; pairIndex < half; pairIndex++)
			{
				var left = players[pairIndex];
				var right = players[players.Count - 1 - pairIndex];
				if (left is not null && right is not null)
				{
					round.Add(roundIndex % 2 == 0 ? (left, right) : (right, left));
				}
			}

			rounds.Add(round);
			var rotated = players[1];
			players.RemoveAt(1);
			players.Add(rotated);
		}

		return rounds;
	}

	private static TournamentRound BuildNextRound(IEnumerable<TournamentParticipant> participants, int roundNumber) => new()
	{
		RoundNumber = roundNumber,
		Scores = participants.Select(participant => new TournamentRoundScore { LeagueMemberId = participant.LeagueMemberId }).ToList()
	};

	private void AdvanceBracket(Tournament tournament, TournamentMatch completedMatch, Guid userId)
	{
		if (completedMatch.RoundNumber == 0)
		{
			AdvanceLeagueStage(tournament);
			return;
		}

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
		var nextRoundMatchCount = (int)Math.Ceiling(winners.Length / 2m);
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
				FramesOrLegs = GetFramesOrLegsForRound(tournament, nextRoundNumber, nextRoundMatchCount),
				WinnerMemberId = playerTwo is null ? playerOne : null,
				Status = playerTwo is null ? TournamentMatchStatus.Completed : TournamentMatchStatus.Ready,
				CompletedAt = playerTwo is null ? DateTime.UtcNow : null
			});
		}
	}

	private static void AdvanceLeagueStage(Tournament tournament)
	{
		var leagueMatches = tournament.Matches.Where(match => match.RoundNumber == 0).ToArray();
		if (leagueMatches.Length == 0 ||
			leagueMatches.Any(match => match.Status != TournamentMatchStatus.Completed) ||
			tournament.Matches.Any(match => match.RoundNumber > 0))
		{
			return;
		}

		var standings = BuildLeagueStandings(tournament, leagueMatches)
			.GroupBy(standing => standing.GroupName)
			.SelectMany(group => group.Take(tournament.QualifiersPerGroup))
			.Select((standing, index) => new TournamentParticipant
			{
				LeagueMemberId = standing.Participant.LeagueMemberId,
				DisplayName = standing.Participant.DisplayName,
				Seed = index + 1,
				IsEliminated = standing.Participant.IsEliminated,
				TotalScore = standing.Participant.TotalScore
			})
			.ToList();
		tournament.Matches.AddRange(BuildOpeningBracket(tournament, standings));
	}

	private static List<LeagueStanding> BuildLeagueStandings(Tournament tournament, IReadOnlyCollection<TournamentMatch> leagueMatches)
	{
		var standings = tournament.Participants.ToDictionary(
			participant => participant.LeagueMemberId,
			participant => new LeagueStanding(participant, string.Empty, 0, 0, 0, 0));

		foreach (var match in leagueMatches.Where(match => match.Status == TournamentMatchStatus.Completed))
		{
			if (!match.PlayerOneMemberId.HasValue || !match.PlayerTwoMemberId.HasValue)
			{
				continue;
			}

			var playerOne = standings[match.PlayerOneMemberId.Value];
			var playerTwo = standings[match.PlayerTwoMemberId.Value];
			playerOne = playerOne with
			{
				GroupName = match.GroupName ?? string.Empty,
				Played = playerOne.Played + 1,
				Wins = playerOne.Wins + (match.WinnerMemberId == match.PlayerOneMemberId ? 1 : 0),
				PointsFor = playerOne.PointsFor + (match.PlayerOneScore ?? 0),
				PointsAgainst = playerOne.PointsAgainst + (match.PlayerTwoScore ?? 0)
			};
			playerTwo = playerTwo with
			{
				GroupName = match.GroupName ?? string.Empty,
				Played = playerTwo.Played + 1,
				Wins = playerTwo.Wins + (match.WinnerMemberId == match.PlayerTwoMemberId ? 1 : 0),
				PointsFor = playerTwo.PointsFor + (match.PlayerTwoScore ?? 0),
				PointsAgainst = playerTwo.PointsAgainst + (match.PlayerOneScore ?? 0)
			};
			standings[match.PlayerOneMemberId.Value] = playerOne;
			standings[match.PlayerTwoMemberId.Value] = playerTwo;
		}

		return standings.Values
			.GroupBy(standing => standing.GroupName)
			.SelectMany(group => group
			.OrderByDescending(standing => standing.Wins)
			.ThenByDescending(standing => standing.PointsFor - standing.PointsAgainst)
			.ThenByDescending(standing => standing.PointsFor)
			.ThenBy(standing => standing.Participant.Seed))
			.ToList();
	}

	private static int GetFramesOrLegsForRound(Tournament tournament, int roundNumber, int matchCount)
	{
		var stageRule = matchCount switch
		{
			1 => tournament.RoundRules.LastOrDefault(rule => rule.RoundNumber == -1),
			2 => tournament.RoundRules.LastOrDefault(rule => rule.RoundNumber == -2),
			4 => tournament.RoundRules.LastOrDefault(rule => rule.RoundNumber == -3),
			_ => null
		};

		return stageRule?.FramesOrLegs
			?? tournament.RoundRules.LastOrDefault(rule => rule.RoundNumber == roundNumber)?.FramesOrLegs
			?? tournament.FramesOrLegs;
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

	private sealed record LeagueStanding(TournamentParticipant Participant, string GroupName, int Played, int Wins, int PointsFor, int PointsAgainst);

	private void UpdatePubGolfTotals(Tournament tournament, Guid userId)
	{
		foreach (var participant in tournament.Participants)
		{
			participant.TotalScore = tournament.PubGolfScores
				.Where(score => score.LeagueMemberId == participant.LeagueMemberId && score.Score.HasValue)
				.Sum(score => score.Score!.Value);
		}

		var allScoresEntered = tournament.PubGolfScores.Count != 0 && tournament.PubGolfScores.All(score => score.Score.HasValue);
		if (!allScoresEntered)
		{
			return;
		}

		var winner = tournament.Participants
			.OrderBy(participant => participant.TotalScore)
			.ThenBy(participant => participant.Seed)
			.First();
		CompleteTournament(tournament, winner.LeagueMemberId, userId);
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
			tournament.Structure,
			tournament.MatchRule,
			tournament.FramesOrLegs,
			tournament.GroupSize,
			tournament.QualifiersPerGroup,
			tournament.RoundRules,
			tournament.PoolRules,
			tournament.BreakRule,
			tournament.CallShotRequired,
			tournament.AllowRerack,
			tournament.PushOutAfterFouls,
			tournament.DoubleInRequired,
			tournament.DoubleOutRequired,
			tournament.StartScore,
			tournament.MinimumPlayers,
			tournament.RoundTimeLimitMinutes,
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
			tournament.Rounds,
			tournament.PubGolfHoles,
			tournament.PubGolfScores);
	}
}

file static class PubGolfTournamentDefaults
{
	public static void HolesOrDefault(this Tournament tournament)
	{
		if (tournament.PubGolfHoles.Count == 0)
		{
			tournament.PubGolfHoles = Enumerable.Range(1, 9).Select(index => new PubGolfHole
			{
				HoleNumber = index,
				Venue = $"Hole {index}",
				Drink = "House drink",
				Par = 3
			}).ToList();
		}

		tournament.PubGolfHoles = tournament.PubGolfHoles
			.OrderBy(hole => hole.HoleNumber)
			.Select((hole, index) =>
			{
				hole.HoleNumber = index + 1;
				hole.Id = hole.Id == Guid.Empty ? Guid.NewGuid() : hole.Id;
				hole.Venue = hole.Venue.Trim();
				hole.Drink = hole.Drink.Trim();
				hole.Par = Math.Max(1, hole.Par);
				return hole;
			})
			.ToList();
	}
}

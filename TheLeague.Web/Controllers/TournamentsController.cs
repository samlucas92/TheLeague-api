using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Web.Extensions;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/leagues/{leagueId:guid}/tournaments")]
public class TournamentsController(ITournamentService tournamentService) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> List(Guid leagueId, CancellationToken cancellationToken) =>
		Ok(await tournamentService.ListAsync(leagueId, User.GetRequiredUserId(), cancellationToken));

	[HttpPost]
	public async Task<IActionResult> Create(Guid leagueId, CreateTournamentRequest request, CancellationToken cancellationToken)
	{
		var format = request.Format ?? (request.GameType == TournamentGameType.PubGolf
			? TournamentFormat.PubGolfCourse
			: request.GameType == TournamentGameType.DartsHighestScore
			? TournamentFormat.RoundElimination
			: TournamentFormat.SingleEliminationBracket);

		var tournament = new Tournament
		{
			Name = request.Name,
			GameType = request.GameType,
			Format = format,
			Structure = request.Structure ?? TournamentStructure.KnockoutOnly,
			MatchRule = request.MatchRule ?? TournamentMatchRule.FirstTo,
			FramesOrLegs = request.FramesOrLegs ?? 5,
			GroupSize = request.GroupSize ?? 4,
			QualifiersPerGroup = request.QualifiersPerGroup ?? 2,
			RoundRules = request.RoundRules?.Select(rule => new TournamentRoundRule
			{
				RoundNumber = rule.RoundNumber,
				FramesOrLegs = rule.FramesOrLegs
			}).ToList() ?? [],
			PoolRules = request.PoolRules?.Where(rule => !string.IsNullOrWhiteSpace(rule)).Select(rule => rule.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [],
			BreakRule = request.BreakRule ?? PoolBreakRule.NormalBreak,
			CallShotRequired = request.CallShotRequired,
			AllowRerack = request.AllowRerack,
			PushOutAfterFouls = request.PushOutAfterFouls,
			DoubleInRequired = request.DoubleInRequired,
			DoubleOutRequired = request.DoubleOutRequired,
			StartScore = request.StartScore,
			MinimumPlayers = request.MinimumPlayers ?? 2,
			RoundTimeLimitMinutes = request.RoundTimeLimitMinutes,
			WinnerPoints = request.WinnerPoints,
			RunnerUpPoints = request.RunnerUpPoints,
			MatchWinPoints = request.MatchWinPoints,
			EliminatePerRound = request.EliminatePerRound,
			ChallengeId = request.ChallengeId,
			PubGolfHoles = request.PubGolfHoles?.Select((hole, index) => new PubGolfHole
			{
				Id = Guid.NewGuid(),
				HoleNumber = index + 1,
				Venue = hole.Venue.Trim(),
				Drink = hole.Drink.Trim(),
				Par = hole.Par,
				HoleRule = string.IsNullOrWhiteSpace(hole.HoleRule) ? null : hole.HoleRule.Trim(),
				Hazard = string.IsNullOrWhiteSpace(hole.Hazard) ? null : hole.Hazard.Trim(),
				Penalty = hole.Penalty,
				Notes = string.IsNullOrWhiteSpace(hole.Notes) ? null : hole.Notes.Trim()
			}).ToList() ?? []
		};

		var created = await tournamentService.CreateAsync(leagueId, User.GetRequiredUserId(), tournament, request.ParticipantMemberIds, cancellationToken);
		return Created($"/api/leagues/{leagueId}/tournaments/{created.Id}", created);
	}

	[HttpPost("{tournamentId:guid}/matches/{matchId:guid}/complete")]
	public async Task<IActionResult> CompleteMatch(Guid leagueId, Guid tournamentId, Guid matchId, CompleteTournamentMatchRequest request, CancellationToken cancellationToken) =>
		Ok(await tournamentService.CompleteMatchAsync(leagueId, User.GetRequiredUserId(), tournamentId, matchId, request.WinnerMemberId, request.PlayerOneScore, request.PlayerTwoScore, cancellationToken));

	[HttpPost("{tournamentId:guid}/rounds/score")]
	public async Task<IActionResult> ScoreRound(Guid leagueId, Guid tournamentId, ScoreTournamentRoundRequest request, CancellationToken cancellationToken) =>
		Ok(await tournamentService.ScoreRoundAsync(leagueId, User.GetRequiredUserId(), tournamentId, request.Scores, cancellationToken));

	[HttpPost("{tournamentId:guid}/pub-golf/scores")]
	public async Task<IActionResult> ScorePubGolfHole(Guid leagueId, Guid tournamentId, ScorePubGolfHoleRequest request, CancellationToken cancellationToken) =>
		Ok(await tournamentService.ScorePubGolfHoleAsync(leagueId, User.GetRequiredUserId(), tournamentId, request.HoleId, request.Scores, cancellationToken));

	[HttpDelete("{tournamentId:guid}")]
	public async Task<IActionResult> Delete(Guid leagueId, Guid tournamentId, CancellationToken cancellationToken)
	{
		await tournamentService.DeleteAsync(leagueId, User.GetRequiredUserId(), tournamentId, cancellationToken);
		return NoContent();
	}
}

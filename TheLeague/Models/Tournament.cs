using TheLeague.Enums;

namespace TheLeague.Models;

public class Tournament
{
	public Guid Id { get; set; }
	public Guid LeagueId { get; set; }
	public Guid CreatedByUserId { get; set; }
	public Guid? ChallengeId { get; set; }
	public string Name { get; set; } = string.Empty;
	public TournamentGameType GameType { get; set; }
	public TournamentFormat Format { get; set; }
	public TournamentStatus Status { get; set; }
	public List<TournamentParticipant> Participants { get; set; } = [];
	public List<TournamentMatch> Matches { get; set; } = [];
	public List<TournamentRound> Rounds { get; set; } = [];
	public int WinnerPoints { get; set; }
	public int RunnerUpPoints { get; set; }
	public int MatchWinPoints { get; set; }
	public int EliminatePerRound { get; set; } = 1;
	public Guid? WinnerMemberId { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime? CompletedAt { get; set; }
	public DateTime? UpdatedAt { get; set; }
}

public class TournamentParticipant
{
	public Guid LeagueMemberId { get; set; }
	public string DisplayName { get; set; } = string.Empty;
	public int Seed { get; set; }
	public bool IsEliminated { get; set; }
	public int TotalScore { get; set; }
}

public class TournamentMatch
{
	public Guid Id { get; set; }
	public int RoundNumber { get; set; }
	public int MatchNumber { get; set; }
	public Guid? PlayerOneMemberId { get; set; }
	public Guid? PlayerTwoMemberId { get; set; }
	public int? PlayerOneScore { get; set; }
	public int? PlayerTwoScore { get; set; }
	public Guid? WinnerMemberId { get; set; }
	public TournamentMatchStatus Status { get; set; }
	public DateTime? CompletedAt { get; set; }
}

public class TournamentRound
{
	public int RoundNumber { get; set; }
	public bool IsComplete { get; set; }
	public List<TournamentRoundScore> Scores { get; set; } = [];
	public List<Guid> EliminatedMemberIds { get; set; } = [];
	public Guid? RoundWinnerMemberId { get; set; }
	public DateTime? CompletedAt { get; set; }
}

public class TournamentRoundScore
{
	public Guid LeagueMemberId { get; set; }
	public int? Score { get; set; }
}

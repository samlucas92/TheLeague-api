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
	public TournamentStructure Structure { get; set; } = TournamentStructure.KnockoutOnly;
	public TournamentMatchRule MatchRule { get; set; } = TournamentMatchRule.FirstTo;
	public int FramesOrLegs { get; set; } = 5;
	public int GroupSize { get; set; } = 4;
	public int QualifiersPerGroup { get; set; } = 2;
	public List<TournamentRoundRule> RoundRules { get; set; } = [];
	public List<string> PoolRules { get; set; } = [];
	public PoolBreakRule BreakRule { get; set; } = PoolBreakRule.NormalBreak;
	public bool CallShotRequired { get; set; }
	public bool AllowRerack { get; set; }
	public bool PushOutAfterFouls { get; set; }
	public bool DoubleInRequired { get; set; } = true;
	public bool DoubleOutRequired { get; set; } = true;
	public int? StartScore { get; set; }
	public int MinimumPlayers { get; set; } = 2;
	public int? RoundTimeLimitMinutes { get; set; }
	public TournamentStatus Status { get; set; }
	public List<TournamentParticipant> Participants { get; set; } = [];
	public List<TournamentMatch> Matches { get; set; } = [];
	public List<TournamentRound> Rounds { get; set; } = [];
	public List<PubGolfHole> PubGolfHoles { get; set; } = [];
	public List<PubGolfScore> PubGolfScores { get; set; } = [];
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
	public string? GroupName { get; set; }
	public Guid? PlayerOneMemberId { get; set; }
	public Guid? PlayerTwoMemberId { get; set; }
	public int? FramesOrLegs { get; set; }
	public int? PlayerOneScore { get; set; }
	public int? PlayerTwoScore { get; set; }
	public Guid? WinnerMemberId { get; set; }
	public TournamentMatchStatus Status { get; set; }
	public DateTime? CompletedAt { get; set; }
}

public class TournamentRoundRule
{
	public int RoundNumber { get; set; }
	public int FramesOrLegs { get; set; }
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

public class PubGolfHole
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public int HoleNumber { get; set; }
	public string Venue { get; set; } = string.Empty;
	public string Drink { get; set; } = string.Empty;
	public int Par { get; set; } = 3;
	public string? HoleRule { get; set; }
	public string? Hazard { get; set; }
	public int? Penalty { get; set; }
	public string? Notes { get; set; }
}

public class PubGolfScore
{
	public Guid LeagueMemberId { get; set; }
	public Guid HoleId { get; set; }
	public int? Score { get; set; }
}

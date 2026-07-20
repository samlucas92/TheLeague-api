using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;
using TheLeague.Mongo.Services;

namespace TheLeague.Tests;

public class VerticalSliceTests
{
	private LeagueDataContext _context = null!;
	private IUserService _users = null!;
	private ILeagueService _leagues = null!;
	private ILeagueMemberService _members = null!;
	private IChallengeService _challenges = null!;
	private IPointSubmissionService _submissions = null!;
	private ILeaderboardService _leaderboard = null!;
	private IPointAllocationService _allocations = null!;

	[SetUp]
	public void SetUp()
	{
		_context = new LeagueDataContext();
		_users = new UserService(_context);
		var authorisation = new LeagueAuthorisationService(_context);
		_members = new LeagueMemberService(_context, authorisation);
		var presets = new LeaguePresetService();
		_challenges = new ChallengeService(_context, authorisation);
		_submissions = new PointSubmissionService(_context, authorisation);
		_leaderboard = new LeaderboardService(_context, authorisation);
		_allocations = new PointAllocationService(_context, authorisation);
		_leagues = new LeagueService(_context, _members, presets, _leaderboard, _allocations, _challenges);
	}

	[Test]
	public async Task FirstVerticalSliceCreatesAllocationLeaderboardAndFeed()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", "A friendly test league.", LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var joinedMember = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		var challenge = await _challenges.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Win a game",
			TargetMemberIds = [joinedMember.Id],
			PointsForSuccess = 20,
			PointsForFailure = -10
		});

		var submission = await _submissions.CreateAsync(league.Id, participant.Id, challenge.Id, null, null, "Won the pool tournament");
		var approval = await _submissions.ApproveAsync(league.Id, owner.Id, submission.Id, null, "Won the pool tournament", null);
		var leaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);
		var feed = await _allocations.ListFeedAsync(league.Id, participant.Id);

		Assert.That(joinedMember.Status, Is.EqualTo(LeagueMemberStatus.Active));
		Assert.That(approval.Allocation.Points, Is.EqualTo(20));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == joinedMember.Id).ApprovedPoints, Is.EqualTo(20));
		Assert.That(feed.Single().Reason, Is.EqualTo("Won the pool tournament"));
	}

	[Test]
	public async Task DuplicateEmailIsRejected()
	{
		await _users.RegisterAsync("Sam", "sam@example.com", "password123");

		Assert.ThrowsAsync<InvalidOperationException>(() => _users.RegisterAsync("Other Sam", "SAM@example.com", "password123"));
	}

	[Test]
	public async Task LoginChecksAllDuplicateEmailRowsForMatchingPassword()
	{
		var original = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var duplicateId = Guid.NewGuid();
		_context.Users[duplicateId] = new UserAccount
		{
			Id = duplicateId,
			Name = "Duplicate Sam",
			EmailAddress = "sam@example.com",
			PasswordHash = PasswordHasher.Hash("different-password"),
			CreatedAt = DateTime.UtcNow.AddMinutes(1)
		};

		var account = await _users.ValidateCredentialsAsync("sam@example.com", "password123");

		Assert.That(account?.Id, Is.EqualTo(original.Id));
	}

	[Test]
	public async Task ParticipantCannotApproveSubmission()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var joinedMember = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		var challenge = await _challenges.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Win a game",
			TargetMemberIds = [joinedMember.Id],
			PointsForSuccess = 20,
			PointsForFailure = -10
		});
		var submission = await _submissions.CreateAsync(league.Id, participant.Id, challenge.Id, null, null, "Won");

		Assert.ThrowsAsync<UnauthorizedAccessException>(() => _submissions.ApproveAsync(league.Id, participant.Id, submission.Id, null, "Approved", null));
	}

	[Test]
	public async Task StagPresetDoesNotCreateChallengeRows()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Stag Weekend", null, LeaguePresetType.Stag, LeagueJoinMode.OpenWithCode);

		var challenges = await _challenges.ListAsync(league.Id, owner.Id);

		Assert.That(challenges, Is.Empty);
	}

	[Test]
	public async Task AdminCanAddPointsWithoutAChallenge()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");

		var award = await _allocations.CreateManualAsync(league.Id, owner.Id, member.Id, 15, "Helped set up");
		var penalty = await _allocations.CreateManualAsync(league.Id, owner.Id, member.Id, -5, "Late arrival");
		var leaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);
		var feed = await _allocations.ListFeedAsync(league.Id, owner.Id);

		Assert.That(award.ChallengeId, Is.Null);
		Assert.That(penalty.Source, Is.EqualTo(PointAllocationSource.AdminPenalty));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == member.Id).ApprovedPoints, Is.EqualTo(10));
		Assert.That(feed.Select(item => item.Reason), Does.Contain("Helped set up"));
		Assert.That(feed.Select(item => item.Reason), Does.Contain("Late arrival"));
	}

	[Test]
	public async Task ParticipantCannotAddManualPoints()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");

		Assert.ThrowsAsync<UnauthorizedAccessException>(() => _allocations.CreateManualAsync(league.Id, participant.Id, member.Id, 10, "Trying it"));
	}

	[Test]
	public async Task ChallengeCanBeAcceptedOrRejectedByTargets()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		var challenge = await _challenges.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Sing karaoke",
			Description = "One full song.",
			TargetMemberIds = [member.Id],
			PointsForSuccess = 25,
			PointsForFailure = -10
		});

		var accepted = await _challenges.AcceptAsync(league.Id, participant.Id, challenge.Id);
		Assert.That(accepted.AcceptedMemberIds, Does.Contain(member.Id));

		var rejection = await _challenges.RejectAsync(league.Id, participant.Id, challenge.Id);
		var leaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);

		Assert.That(rejection.Challenge.RejectedMemberIds, Does.Contain(member.Id));
		Assert.That(rejection.Penalty.Points, Is.EqualTo(-10));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == member.Id).ApprovedPoints, Is.EqualTo(-10));
	}

	[Test]
	public async Task ChallengeCanBeCompletedOrFailedByTargets()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		var completedChallenge = await _challenges.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Sing karaoke",
			TargetMemberIds = [member.Id],
			PointsForSuccess = 25,
			PointsForFailure = -10
		});
		var failedChallenge = await _challenges.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Do a backflip",
			TargetMemberIds = [member.Id],
			PointsForSuccess = 30,
			PointsForFailure = -15
		});

		var completion = await _challenges.CompleteAsync(league.Id, participant.Id, completedChallenge.Id);
		var failure = await _challenges.FailAsync(league.Id, participant.Id, failedChallenge.Id);
		var leaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);
		var feed = await _allocations.ListFeedAsync(league.Id, owner.Id);

		Assert.That(completion.Allocation.Points, Is.EqualTo(25));
		Assert.That(completion.Challenge.CompletedMemberIds, Does.Contain(member.Id));
		Assert.That(failure.Allocation.Points, Is.EqualTo(-15));
		Assert.That(failure.Challenge.FailedMemberIds, Does.Contain(member.Id));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == member.Id).ApprovedPoints, Is.EqualTo(10));
		Assert.That(feed.Select(item => item.Reason), Does.Contain("Completed challenge: Sing karaoke"));
		Assert.That(feed.Select(item => item.Reason), Does.Contain("Failed challenge: Do a backflip"));
	}

	[Test]
	public async Task PublicViewCanBeLoadedByJoinCodeWithoutMembership()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Public League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var offline = await _members.AddOfflineMemberAsync(league.Id, owner.Id, "Kyle", null, LeagueMemberRole.Participant);
		await _allocations.CreateManualAsync(league.Id, owner.Id, offline.Id, 12, "Bonus");

		var view = await _leagues.GetPublicViewAsync(league.JoinCode);

		Assert.That(view.League.Id, Is.EqualTo(league.Id));
		Assert.That(view.Members.Select(member => member.DisplayName), Does.Contain("Kyle"));
		Assert.That(view.Leaderboard.Single(row => row.LeagueMemberId == offline.Id).ApprovedPoints, Is.EqualTo(12));
		Assert.That(view.PointsFeed.Single().Reason, Is.EqualTo("Bonus"));
	}

	[Test]
	public async Task PointApproverCanApproveButCannotAddManualPoints()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var approverUser = await _users.RegisterAsync("Amy", "amy@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var approverMember = await _members.JoinAsync(approverUser.Id, league.JoinCode, "Amy");
		var participantMember = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		await _members.ChangeRoleAsync(league.Id, owner.Id, approverMember.Id, LeagueMemberRole.PointApprover);
		var challenge = await _challenges.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Win a game",
			TargetMemberIds = [participantMember.Id],
			PointsForSuccess = 10,
			PointsForFailure = -5
		});
		var submission = await _submissions.CreateAsync(league.Id, participant.Id, challenge.Id, null, null, "Won");

		var approval = await _submissions.ApproveAsync(league.Id, approverUser.Id, submission.Id, null, "Won", null);

		Assert.That(approval.Allocation.Points, Is.EqualTo(10));
		Assert.ThrowsAsync<UnauthorizedAccessException>(() => _allocations.CreateManualAsync(league.Id, approverUser.Id, participantMember.Id, 5, "Manual"));
	}

	[Test]
	public async Task OfflineMemberCanBeLinkedAndPointsMergeIntoExistingRegisteredMember()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Kyle", "kyle@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var registeredMember = await _members.JoinAsync(participant.Id, league.JoinCode, "Kyle Registered");
		var offlineMember = await _members.AddOfflineMemberAsync(league.Id, owner.Id, "Kyle Offline", "offline@example.com", LeagueMemberRole.Participant);
		await _allocations.CreateManualAsync(league.Id, owner.Id, offlineMember.Id, 20, "Offline points");

		var linkedMember = await _members.LinkOfflineMemberAsync(league.Id, owner.Id, offlineMember.Id, participant.EmailAddress);
		var leaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);

		Assert.That(linkedMember.Id, Is.EqualTo(registeredMember.Id));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == registeredMember.Id).ApprovedPoints, Is.EqualTo(20));
		Assert.That(_context.Members[offlineMember.Id].Status, Is.EqualTo(LeagueMemberStatus.Removed));
	}
}

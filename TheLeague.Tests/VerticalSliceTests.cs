using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;
using TheLeague.Mongo.Services;
using Microsoft.Extensions.Options;

namespace TheLeague.Tests;

public class VerticalSliceTests
{
	private LeagueDataContext _context = null!;
	private IUserService _users = null!;
	private ILeagueService _leagues = null!;
	private ILeagueMemberService _members = null!;
	private IChallengeService _challenges = null!;
	private IPointSubmissionService _submissions = null!;
	private ITournamentService _tournaments = null!;
	private ILeaderboardService _leaderboard = null!;
	private IPointAllocationService _allocations = null!;
	private ILeagueAuditService _audit = null!;

	[SetUp]
	public void SetUp()
	{
		_context = new LeagueDataContext();
		_users = new UserService(_context);
		var authorisation = new LeagueAuthorisationService(_context);
		_audit = new LeagueAuditService(_context, authorisation);
		_members = new LeagueMemberService(_context, authorisation, _audit);
		var presets = new LeaguePresetService();
		_challenges = new ChallengeService(_context, authorisation, _audit);
		_tournaments = new TournamentService(_context, authorisation, _audit);
		_submissions = new PointSubmissionService(_context, authorisation, _audit);
		_leaderboard = new LeaderboardService(_context, authorisation);
		_allocations = new PointAllocationService(_context, authorisation, _audit);
		_leagues = new LeagueService(_context, _members, presets, _leaderboard, _allocations, _challenges, _tournaments, authorisation, _audit);
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
		Assert.That(approval.Allocation.AwardedByUserId, Is.EqualTo(participant.Id));
		Assert.That(approval.Submission.ReviewedByUserId, Is.EqualTo(owner.Id));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == joinedMember.Id).ApprovedPoints, Is.EqualTo(20));
		Assert.That(feed.Single().Reason, Is.EqualTo("Won the pool tournament"));
		Assert.That(feed.Single().AwardedByName, Is.EqualTo("Tom"));
	}

	[Test]
	public async Task TournamentsCanRunPoolBracketAndDartsRoundElimination()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var playerTwo = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var playerThree = await _users.RegisterAsync("Kyle", "kyle@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Pub games", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var ownerMember = (await _members.ListAsync(league.Id, owner.Id)).Single(member => member.UserId == owner.Id);
		var tom = await _members.JoinAsync(playerTwo.Id, league.JoinCode, "Tom");
		var kyle = await _members.JoinAsync(playerThree.Id, league.JoinCode, "Kyle");

		var pool = await _tournaments.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Pool knockout",
			GameType = TournamentGameType.Pool,
			Format = TournamentFormat.SingleEliminationBracket,
			WinnerPoints = 20,
			RunnerUpPoints = 5,
			MatchWinPoints = 2
		}, [ownerMember.Id, tom.Id], CancellationToken.None);

		var poolMatch = pool.Matches.Single();
		var completedPool = await _tournaments.CompleteMatchAsync(league.Id, owner.Id, pool.Id, poolMatch.Id, tom.Id, 1, 2);
		Assert.That(completedPool.Status, Is.EqualTo(TournamentStatus.Completed));
		Assert.That(completedPool.WinnerMemberId, Is.EqualTo(tom.Id));

		var darts = await _tournaments.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Highest score darts",
			GameType = TournamentGameType.DartsHighestScore,
			Format = TournamentFormat.RoundElimination,
			WinnerPoints = 15,
			EliminatePerRound = 1
		}, [ownerMember.Id, tom.Id, kyle.Id], CancellationToken.None);

		await _tournaments.ScoreRoundAsync(league.Id, owner.Id, darts.Id, new Dictionary<Guid, int>
		{
			[ownerMember.Id] = 40,
			[tom.Id] = 60,
			[kyle.Id] = 20
		});
		var completedDarts = await _tournaments.ScoreRoundAsync(league.Id, owner.Id, darts.Id, new Dictionary<Guid, int>
		{
			[ownerMember.Id] = 80,
			[tom.Id] = 70
		});

		Assert.That(completedDarts.Status, Is.EqualTo(TournamentStatus.Completed));
		Assert.That(completedDarts.WinnerMemberId, Is.EqualTo(ownerMember.Id));
		var leaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == tom.Id).ApprovedPoints, Is.EqualTo(22));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == ownerMember.Id).ApprovedPoints, Is.EqualTo(20));
	}

	[Test]
	public async Task Darts501TournamentIsStoredAsDarts()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var playerTwo = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Darts league", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var ownerMember = (await _members.ListAsync(league.Id, owner.Id)).Single(member => member.UserId == owner.Id);
		var tom = await _members.JoinAsync(playerTwo.Id, league.JoinCode, "Tom");

		var darts = await _tournaments.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "501 Darts Knockout",
			GameType = TournamentGameType.Darts501,
			Format = TournamentFormat.SingleEliminationBracket,
			Structure = TournamentStructure.LeagueAndKnockout,
			WinnerPoints = 20
		}, [ownerMember.Id, tom.Id], CancellationToken.None);

		Assert.That(darts.GameType, Is.EqualTo(TournamentGameType.Darts501));
		Assert.That(darts.Format, Is.EqualTo(TournamentFormat.SingleEliminationBracket));
		Assert.That(darts.Structure, Is.EqualTo(TournamentStructure.LeagueAndKnockout));
		Assert.That(darts.Matches, Has.All.Property(nameof(TournamentMatch.RoundNumber)).EqualTo(0));

		var leagueMatch = darts.Matches.Single();
		var afterLeague = await _tournaments.CompleteMatchAsync(
			league.Id,
			owner.Id,
			darts.Id,
			leagueMatch.Id,
			ownerMember.Id,
			4,
			2,
			CancellationToken.None);

		Assert.That(afterLeague.Matches.Any(match => match.RoundNumber == 1), Is.True);
	}

	[Test]
	public async Task PubGolfUsesCourseScorecardsAndLowestScoreWins()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var playerTwo = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Pub golf league", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var ownerMember = (await _members.ListAsync(league.Id, owner.Id)).Single(member => member.UserId == owner.Id);
		var tom = await _members.JoinAsync(playerTwo.Id, league.JoinCode, "Tom");

		var pubGolf = await _tournaments.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Saturday Night Pub Golf",
			GameType = TournamentGameType.PubGolf,
			Format = TournamentFormat.PubGolfCourse,
			WinnerPoints = 12,
			PubGolfHoles =
			[
				new PubGolfHole { Venue = "Old Crown", Drink = "Pint of Lager", Par = 4 },
				new PubGolfHole { Venue = "The Griffin", Drink = "Bottle of cider", Par = 3, Hazard = "Water Hazard", Penalty = 2 }
			]
		}, [ownerMember.Id, tom.Id], CancellationToken.None);

		Assert.That(pubGolf.Matches, Is.Empty);
		Assert.That(pubGolf.Rounds, Is.Empty);
		Assert.That(pubGolf.PubGolfHoles.Count, Is.EqualTo(2));

		pubGolf = await _tournaments.ScorePubGolfHoleAsync(league.Id, owner.Id, pubGolf.Id, pubGolf.PubGolfHoles.First().Id, new Dictionary<Guid, int?>
		{
			[ownerMember.Id] = 3,
			[tom.Id] = 5
		});

		Assert.That(pubGolf.Status, Is.EqualTo(TournamentStatus.Active));

		var completed = await _tournaments.ScorePubGolfHoleAsync(league.Id, owner.Id, pubGolf.Id, pubGolf.PubGolfHoles.Last().Id, new Dictionary<Guid, int?>
		{
			[ownerMember.Id] = 4,
			[tom.Id] = 3
		});

		Assert.That(completed.Status, Is.EqualTo(TournamentStatus.Completed));
		Assert.That(completed.WinnerMemberId, Is.EqualTo(ownerMember.Id));
		Assert.That(completed.Participants.Single(participant => participant.LeagueMemberId == ownerMember.Id).TotalScore, Is.EqualTo(7));
		var leaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == ownerMember.Id).ApprovedPoints, Is.EqualTo(12));
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
	public async Task LoginMatchesStoredEmailCaseInsensitively()
	{
		var accountId = Guid.NewGuid();
		_context.Users[accountId] = new UserAccount
		{
			Id = accountId,
			Name = "Sam",
			EmailAddress = "SAM@example.com",
			PasswordHash = PasswordHasher.Hash("password123"),
			CreatedAt = DateTime.UtcNow
		};

		var account = await _users.ValidateCredentialsAsync("sam@example.com", "password123");

		Assert.That(account?.Id, Is.EqualTo(accountId));
	}

	[Test]
	public async Task SamLucasAccountIsSiteAdmin()
	{
		var account = await _users.RegisterAsync("Sam Lucas", "samlucas92@gmail.com", "password123");

		Assert.That(account.IsSiteAdmin, Is.True);

		account.IsSiteAdmin = false;
		await _context.Users.SaveAsync(account);

		var loaded = await _users.GetByIdAsync(account.Id);
		Assert.That(loaded?.IsSiteAdmin, Is.True);
	}

	[Test]
	public async Task SiteAdminCanListAndPromoteUsers()
	{
		var siteAdmin = await _users.RegisterAsync("Sam Lucas", "samlucas92@gmail.com", "password123");
		var user = await _users.RegisterAsync("Tom", "tom@example.com", "password123");

		var users = await _users.ListForSiteAdminAsync();
		var promoted = await _users.SetSiteAdminAsync(siteAdmin.Id, user.Id, true);

		Assert.That(users.Select(item => item.EmailAddress), Does.Contain("samlucas92@gmail.com"));
		Assert.That(promoted.IsSiteAdmin, Is.True);
		Assert.ThrowsAsync<InvalidOperationException>(() => _users.SetSiteAdminAsync(siteAdmin.Id, siteAdmin.Id, false));
		Assert.ThrowsAsync<UnauthorizedAccessException>(() => _users.SetSiteAdminAsync(Guid.NewGuid(), user.Id, false));
	}

	[Test]
	public async Task UserCanChangePassword()
	{
		var account = await _users.RegisterAsync("Sam", "sam@example.com", "password123");

		await _users.ChangePasswordAsync(account.Id, "password123", "new-password123");

		Assert.That(await _users.ValidateCredentialsAsync("sam@example.com", "password123"), Is.Null);
		Assert.That((await _users.ValidateCredentialsAsync("sam@example.com", "new-password123"))?.Id, Is.EqualTo(account.Id));
		Assert.ThrowsAsync<InvalidOperationException>(() => _users.ChangePasswordAsync(account.Id, "wrong-password", "another-password123"));
		Assert.ThrowsAsync<InvalidOperationException>(() => _users.ChangePasswordAsync(account.Id, "new-password123", "short"));
	}

	[Test]
	public async Task DeactivatedUserCannotLogin()
	{
		var account = await _users.RegisterAsync("Tom", "tom@example.com", "password123");

		Assert.ThrowsAsync<InvalidOperationException>(() => _users.DeactivateAsync(account.Id, "wrong-password"));
		await _users.DeactivateAsync(account.Id, "password123");

		var updated = await _users.GetByIdAsync(account.Id);
		Assert.That(updated?.IsDeleted, Is.True);
		Assert.That(updated?.DeletedAt, Is.Not.Null);
		Assert.That(await _users.ValidateCredentialsAsync("tom@example.com", "password123"), Is.Null);
	}

	[Test]
	public async Task InitialSiteAdminCannotDeactivate()
	{
		var account = await _users.RegisterAsync("Sam Lucas", "samlucas92@gmail.com", "password123");

		Assert.ThrowsAsync<InvalidOperationException>(() => _users.DeactivateAsync(account.Id, "password123"));
	}

	[Test]
	public async Task UserCanResetPasswordWithOneTimeToken()
	{
		var account = await _users.RegisterAsync("Sam", "sam@example.com", "password123");

		var request = await _users.RequestPasswordResetAsync("sam@example.com");
		await _users.ResetPasswordAsync(request.ResetToken!, "reset-password123");

		Assert.That(request.TokenCreated, Is.True);
		Assert.That(request.ResetToken, Is.Not.Null);
		Assert.That(await _users.ValidateCredentialsAsync("sam@example.com", "password123"), Is.Null);
		Assert.That((await _users.ValidateCredentialsAsync("sam@example.com", "reset-password123"))?.Id, Is.EqualTo(account.Id));
		Assert.ThrowsAsync<InvalidOperationException>(() => _users.ResetPasswordAsync(request.ResetToken!, "another-password123"));
	}

	[Test]
	public async Task UnknownPasswordResetEmailDoesNotCreateToken()
	{
		var request = await _users.RequestPasswordResetAsync("missing@example.com");

		Assert.That(request.TokenCreated, Is.False);
		Assert.That(request.ResetToken, Is.Null);
		Assert.That(_context.PasswordResetTokens.Values, Is.Empty);
	}

	[Test]
	public async Task EmailOutboxStoresSendStatusAndFailures()
	{
		var sender = new FakeEmailSender();
		var outbox = new EmailOutboxService(_context, sender, Options.Create(new EmailSettings
		{
			FromEmail = "hello@example.com",
			FromName = "The League"
		}));

		var message = await outbox.QueueAsync("sam@example.com", "Sam", "Subject", "<p>Hello</p>", "Hello");
		Assert.That(message.Status, Is.EqualTo(EmailMessageStatus.Pending));

		var sent = await outbox.SendAsync(message.Id);

		Assert.That(sent.Status, Is.EqualTo(EmailMessageStatus.Sent));
		Assert.That(sent.Attempts, Is.EqualTo(1));
		Assert.That(sent.ProviderMessageId, Is.EqualTo("resend-message-id"));

		sender.FailureReason = "Provider unavailable";
		var failedMessage = await outbox.QueueAsync("tom@example.com", null, "Subject", "<p>Hello</p>", "Hello");
		var failed = await outbox.SendAsync(failedMessage.Id);

		Assert.That(failed.Status, Is.EqualTo(EmailMessageStatus.Failed));
		Assert.That(failed.FailureReason, Is.EqualTo("Provider unavailable"));
		Assert.That(failed.NextAttemptAt, Is.Not.Null);
	}

	[Test]
	public async Task UserCanVerifyEmailWithOneTimeToken()
	{
		var account = await _users.RegisterAsync("Sam", "sam@example.com", "password123");

		var request = await _users.RequestEmailVerificationAsync(account.Id);
		await _users.VerifyEmailAsync(request.VerificationToken!);

		var updated = await _users.GetByIdAsync(account.Id);
		Assert.That(request.TokenCreated, Is.True);
		Assert.That(request.VerificationToken, Is.Not.Null);
		Assert.That(updated?.IsEmailVerified, Is.True);
		Assert.That(updated?.EmailVerifiedAt, Is.Not.Null);
		Assert.ThrowsAsync<InvalidOperationException>(() => _users.VerifyEmailAsync(request.VerificationToken!));
		Assert.That((await _users.RequestEmailVerificationAsync(account.Id)).TokenCreated, Is.False);
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

		Assert.That(award.Status, Is.EqualTo("Approved"));
		Assert.That(award.Allocation?.ChallengeId, Is.Null);
		Assert.That(penalty.Allocation?.Source, Is.EqualTo(PointAllocationSource.AdminPenalty));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == member.Id).ApprovedPoints, Is.EqualTo(10));
		Assert.That(feed.Select(item => item.Reason), Does.Contain("Helped set up"));
		Assert.That(feed.Select(item => item.Reason), Does.Contain("Late arrival"));
	}

	[Test]
	public async Task ParticipantManualPointsCreatePendingSubmission()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");

		var result = await _allocations.CreateManualAsync(league.Id, participant.Id, member.Id, 10, "Trying it");
		var pending = await _submissions.ListPendingAsync(league.Id, owner.Id);

		Assert.That(result.Status, Is.EqualTo("Pending"));
		Assert.That(result.Allocation, Is.Null);
		Assert.That(result.Submission?.ChallengeId, Is.Null);
		Assert.That(pending.Single().ChallengeName, Is.EqualTo("Manual points"));
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
	public async Task ChallengeCreatorCanCompleteOrFailAcceptedChallengesForTargets()
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

		await _challenges.AcceptAsync(league.Id, participant.Id, completedChallenge.Id);
		await _challenges.AcceptAsync(league.Id, participant.Id, failedChallenge.Id);
		var completion = await _challenges.CompleteAsync(league.Id, owner.Id, completedChallenge.Id, member.Id);
		var failure = await _challenges.FailAsync(league.Id, owner.Id, failedChallenge.Id, member.Id);
		var leaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);
		var feed = await _allocations.ListFeedAsync(league.Id, owner.Id);
		var challengeList = await _challenges.ListAsync(league.Id, owner.Id);

		Assert.That(completion.Allocation.Points, Is.EqualTo(25));
		Assert.That(completion.Challenge.CompletedMemberIds, Does.Contain(member.Id));
		Assert.That(failure.Allocation.Points, Is.EqualTo(-15));
		Assert.That(failure.Challenge.FailedMemberIds, Does.Contain(member.Id));
		Assert.That(leaderboard.Single(row => row.LeagueMemberId == member.Id).ApprovedPoints, Is.EqualTo(10));
		Assert.That(feed.Select(item => item.Reason), Does.Contain("Completed challenge: Sing karaoke"));
		Assert.That(feed.Select(item => item.Reason), Does.Contain("Failed challenge: Do a backflip"));
		Assert.That(challengeList.Single(challenge => challenge.Id == completedChallenge.Id).Outcomes.Single().Status, Is.EqualTo("Completed"));
		Assert.That(challengeList.Single(challenge => challenge.Id == failedChallenge.Id).Outcomes.Single().Status, Is.EqualTo("Failed"));
	}

	[Test]
	public async Task TargetCannotConfirmTheirOwnChallengeOutcome()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		var challenge = await _challenges.CreateAsync(league.Id, owner.Id, new()
		{
			Name = "Sing karaoke",
			TargetMemberIds = [member.Id],
			PointsForSuccess = 25,
			PointsForFailure = -10
		});
		await _challenges.AcceptAsync(league.Id, participant.Id, challenge.Id);

		Assert.ThrowsAsync<UnauthorizedAccessException>(() => _challenges.CompleteAsync(league.Id, participant.Id, challenge.Id, member.Id));
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
	public async Task AdminCanUpdateLeagueSettingsAndRegenerateJoinCode()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var originalCode = league.JoinCode;

		var updated = await _leagues.UpdateSettingsAsync(league.Id, owner.Id, "New Name", "Fresh description", LeagueJoinMode.Closed, false);
		var regenerated = await _leagues.RegenerateJoinCodeAsync(league.Id, owner.Id);

		Assert.That(updated.Name, Is.EqualTo("New Name"));
		Assert.That(updated.Description, Is.EqualTo("Fresh description"));
		Assert.That(updated.JoinMode, Is.EqualTo(LeagueJoinMode.Closed));
		Assert.That(updated.PublicViewEnabled, Is.False);
		Assert.That(regenerated.JoinCode, Is.Not.EqualTo(originalCode));
		Assert.ThrowsAsync<InvalidOperationException>(() => _leagues.GetPublicViewAsync(regenerated.JoinCode));
	}

	[Test]
	public async Task PointApproverCanApproveAndTheirManualPointsNeedApproval()
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
		var manualRequest = await _allocations.CreateManualAsync(league.Id, approverUser.Id, participantMember.Id, 5, "Manual");

		Assert.That(approval.Allocation.Points, Is.EqualTo(10));
		Assert.That(manualRequest.Status, Is.EqualTo("Pending"));
		Assert.ThrowsAsync<InvalidOperationException>(() => _submissions.ApproveAsync(league.Id, approverUser.Id, manualRequest.Submission!.Id, 5, "Manual", null));
	}

	[Test]
	public async Task AdminCanEditAndDeletePointAllocations()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		var result = await _allocations.CreateManualAsync(league.Id, owner.Id, member.Id, 10, "Original");

		var updated = await _allocations.UpdateAsync(league.Id, owner.Id, result.Allocation!.Id, member.Id, 25, "Corrected");
		var editedLeaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);
		await _allocations.DeleteAsync(league.Id, owner.Id, result.Allocation!.Id);
		var deletedLeaderboard = await _leaderboard.GetAsync(league.Id, owner.Id);

		Assert.That(updated.Points, Is.EqualTo(25));
		Assert.That(updated.Reason, Is.EqualTo("Corrected"));
		Assert.That(editedLeaderboard.Single(row => row.LeagueMemberId == member.Id).ApprovedPoints, Is.EqualTo(25));
		Assert.That(deletedLeaderboard.Single(row => row.LeagueMemberId == member.Id).ApprovedPoints, Is.EqualTo(0));
	}

	[Test]
	public async Task ChallengeCreatorCanEditAndDeleteChallenge()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		var challenge = await _challenges.CreateAsync(league.Id, participant.Id, new()
		{
			Name = "Sing karaoke",
			TargetMemberIds = [member.Id],
			PointsForSuccess = 10,
			PointsForFailure = -5
		});

		var updated = await _challenges.UpdateAsync(league.Id, participant.Id, challenge.Id, new()
		{
			Name = "Sing two songs",
			TargetMemberIds = [member.Id],
			PointsForSuccess = 20,
			PointsForFailure = -10,
			IsActive = true
		});
		await _challenges.DeleteAsync(league.Id, owner.Id, challenge.Id);
		var challenges = await _challenges.ListAsync(league.Id, owner.Id);

		Assert.That(updated.Name, Is.EqualTo("Sing two songs"));
		Assert.That(updated.PointsForSuccess, Is.EqualTo(20));
		Assert.That(challenges.Select(item => item.Id), Does.Not.Contain(challenge.Id));
	}

	[Test]
	public async Task AdminCanEditAndRemoveMembers()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var adminUser = await _users.RegisterAsync("Amy", "amy@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var adminMember = await _members.JoinAsync(adminUser.Id, league.JoinCode, "Amy");
		await _members.ChangeRoleAsync(league.Id, owner.Id, adminMember.Id, LeagueMemberRole.Admin);
		var offlineMember = await _members.AddOfflineMemberAsync(league.Id, owner.Id, "Kyle", "kyle-old@example.com", LeagueMemberRole.Participant);

		var updated = await _members.UpdateAsync(league.Id, adminUser.Id, offlineMember.Id, "Kyle Morgan", "kyle@example.com", LeagueMemberRole.PointApprover);
		await _members.DeleteAsync(league.Id, adminUser.Id, offlineMember.Id);
		var members = await _members.ListAsync(league.Id, owner.Id);

		Assert.That(updated.DisplayName, Is.EqualTo("Kyle Morgan"));
		Assert.That(updated.EmailAddress, Is.EqualTo("kyle@example.com"));
		Assert.That(updated.Role, Is.EqualTo(LeagueMemberRole.PointApprover));
		Assert.That(_context.Members[offlineMember.Id].Status, Is.EqualTo(LeagueMemberStatus.Removed));
		Assert.That(members.Select(member => member.Id), Does.Not.Contain(offlineMember.Id));
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

	[Test]
	public async Task AdminCanReadRecentAuditEntries()
	{
		var owner = await _users.RegisterAsync("Sam", "sam@example.com", "password123");
		var participant = await _users.RegisterAsync("Tom", "tom@example.com", "password123");
		var league = await _leagues.CreateAsync(owner.Id, "Weekend League", null, LeaguePresetType.Custom, LeagueJoinMode.OpenWithCode);
		var member = await _members.JoinAsync(participant.Id, league.JoinCode, "Tom");
		await _allocations.CreateManualAsync(league.Id, owner.Id, member.Id, 10, "Bonus");
		await _members.ChangeRoleAsync(league.Id, owner.Id, member.Id, LeagueMemberRole.PointApprover);

		var audit = await _audit.ListAsync(league.Id, owner.Id);

		Assert.That(audit.Select(entry => entry.Action), Does.Contain(LeagueAuditAction.PointsAwarded));
		Assert.That(audit.Select(entry => entry.Action), Does.Contain(LeagueAuditAction.RoleChanged));
		Assert.That(audit.First().CreatedAt, Is.GreaterThanOrEqualTo(audit.Last().CreatedAt));
		Assert.ThrowsAsync<UnauthorizedAccessException>(() => _audit.ListAsync(league.Id, participant.Id));
	}

	private sealed class FakeEmailSender : IEmailSender
	{
		public string? FailureReason { get; set; }

		public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
			Task.FromResult(FailureReason is null
				? new EmailSendResult(true, "resend-message-id", null)
				: new EmailSendResult(false, null, FailureReason));
	}
}

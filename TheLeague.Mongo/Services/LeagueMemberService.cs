using TheLeague.Enums;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Mongo.Context;

namespace TheLeague.Mongo.Services;

public class LeagueMemberService(LeagueDataContext context, ILeagueAuthorisationService authorisationService) : ILeagueMemberService
{
	public async Task<IReadOnlyCollection<LeagueMember>> ListAsync(Guid leagueId, Guid userId, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredMembershipAsync(leagueId, userId, cancellationToken);

		return context.Members.Values
			.Where(member => member.LeagueId == leagueId && member.Status is LeagueMemberStatus.Active or LeagueMemberStatus.Pending)
			.OrderBy(member => member.DisplayName)
			.ToArray();
	}

	public Task<LeagueMember> JoinAsync(Guid userId, string joinCode, string displayName, CancellationToken cancellationToken = default)
	{
		var normalizedCode = LeagueService.NormalizeJoinCode(joinCode);
		var league = context.Leagues.Values
			.Where(league => league.JoinCode == normalizedCode)
			.OrderByDescending(league => league.CreatedAt)
			.FirstOrDefault();
		if (league is null)
		{
			throw new InvalidOperationException("Join code was not found.");
		}

		if (league.Status is LeagueStatus.Closed or LeagueStatus.Archived || league.JoinMode is LeagueJoinMode.Closed or LeagueJoinMode.InviteOnly)
		{
			throw new InvalidOperationException("This league is not accepting code joins.");
		}

		if (context.Members.Values.Any(member => member.LeagueId == league.Id && member.UserId == userId && member.Status != LeagueMemberStatus.Removed))
		{
			throw new InvalidOperationException("You are already a member of this league.");
		}

		var name = ValidateDisplayName(league.Id, displayName);
		var activeCount = context.Members.Values.Count(member => member.LeagueId == league.Id && member.Status == LeagueMemberStatus.Active);
		if (league.MaximumParticipants.HasValue && activeCount >= league.MaximumParticipants.Value)
		{
			throw new InvalidOperationException("This league is full.");
		}

		var member = new LeagueMember
		{
			Id = Guid.NewGuid(),
			LeagueId = league.Id,
			UserId = userId,
			DisplayName = name,
			IsOfflineMember = false,
			Role = LeagueMemberRole.Participant,
			Status = league.JoinMode == LeagueJoinMode.ApprovalRequired ? LeagueMemberStatus.Pending : LeagueMemberStatus.Active,
			JoinedAt = DateTime.UtcNow,
			ApprovedAt = league.JoinMode == LeagueJoinMode.OpenWithCode ? DateTime.UtcNow : null
		};

		context.Members[member.Id] = member;
		return Task.FromResult(member);
	}

	public Task<LeagueMember> CreateOwnerAsync(Guid leagueId, Guid ownerUserId, string displayName, CancellationToken cancellationToken = default)
	{
		var member = new LeagueMember
		{
			Id = Guid.NewGuid(),
			LeagueId = leagueId,
			UserId = ownerUserId,
			DisplayName = ValidateDisplayName(leagueId, displayName),
			IsOfflineMember = false,
			Role = LeagueMemberRole.Owner,
			Status = LeagueMemberStatus.Active,
			JoinedAt = DateTime.UtcNow,
			ApprovedAt = DateTime.UtcNow,
			ApprovedByUserId = ownerUserId
		};

		context.Members[member.Id] = member;
		return Task.FromResult(member);
	}

	public async Task<LeagueMember> AddOfflineMemberAsync(Guid leagueId, Guid userId, string displayName, string? emailAddress, LeagueMemberRole role, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var member = new LeagueMember
		{
			Id = Guid.NewGuid(),
			LeagueId = leagueId,
			DisplayName = ValidateDisplayName(leagueId, displayName),
			EmailAddress = NormalizeOptionalEmail(emailAddress),
			IsOfflineMember = true,
			Role = role == LeagueMemberRole.Owner ? LeagueMemberRole.Participant : role,
			Status = LeagueMemberStatus.Active,
			JoinedAt = DateTime.UtcNow,
			ApprovedAt = DateTime.UtcNow,
			ApprovedByUserId = userId
		};

		context.Members[member.Id] = member;
		return member;
	}

	public async Task<LeagueMember> UpdateAsync(Guid leagueId, Guid userId, Guid memberId, string displayName, string? emailAddress, LeagueMemberRole role, CancellationToken cancellationToken = default)
	{
		var adminMembership = await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var member = GetLeagueMember(leagueId, memberId);
		await EnsureCanSetRoleAsync(leagueId, userId, adminMembership, member, role, cancellationToken);

		member.DisplayName = ValidateDisplayName(leagueId, displayName, member.Id);
		member.EmailAddress = NormalizeOptionalEmail(emailAddress);
		member.Role = role;
		await context.Members.SaveAsync(member, cancellationToken);
		return member;
	}

	public async Task<LeagueMember> ChangeRoleAsync(Guid leagueId, Guid userId, Guid memberId, LeagueMemberRole role, CancellationToken cancellationToken = default)
	{
		var adminMembership = await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var member = GetLeagueMember(leagueId, memberId);
		await EnsureCanSetRoleAsync(leagueId, userId, adminMembership, member, role, cancellationToken);

		member.Role = role;
		await context.Members.SaveAsync(member, cancellationToken);
		return member;
	}

	public async Task DeleteAsync(Guid leagueId, Guid userId, Guid memberId, CancellationToken cancellationToken = default)
	{
		var adminMembership = await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var member = GetLeagueMember(leagueId, memberId);
		if (member.UserId == userId)
		{
			throw new InvalidOperationException("You cannot remove yourself.");
		}

		if (member.Role == LeagueMemberRole.Owner || (member.Role == LeagueMemberRole.Admin && adminMembership.Role != LeagueMemberRole.Owner))
		{
			await authorisationService.GetRequiredOwnerMembershipAsync(leagueId, userId, cancellationToken);
		}

		if (member.Role == LeagueMemberRole.Owner)
		{
			EnsureAnotherOwnerRemains(leagueId);
		}

		member.Status = LeagueMemberStatus.Removed;
		await context.Members.SaveAsync(member, cancellationToken);
	}

	public async Task<LeagueMember> LinkOfflineMemberAsync(Guid leagueId, Guid userId, Guid memberId, string emailAddress, CancellationToken cancellationToken = default)
	{
		await authorisationService.GetRequiredAdminMembershipAsync(leagueId, userId, cancellationToken);
		var offlineMember = GetLeagueMember(leagueId, memberId);
		if (!offlineMember.IsOfflineMember || offlineMember.UserId.HasValue)
		{
			throw new InvalidOperationException("Only offline members can be linked.");
		}

		var normalizedEmail = UserService.NormalizeEmail(emailAddress);
		var account = context.Users.Values
			.Where(user => user.EmailAddress == normalizedEmail)
			.OrderByDescending(user => user.CreatedAt)
			.FirstOrDefault();
		if (account is null)
		{
			throw new InvalidOperationException("No registered user exists with that email address.");
		}

		var existingMember = context.Members.Values
			.Where(member =>
				member.LeagueId == leagueId &&
				member.UserId == account.Id &&
				member.Status == LeagueMemberStatus.Active)
			.OrderByDescending(member => member.JoinedAt)
			.FirstOrDefault();
		if (existingMember is null)
		{
			offlineMember.UserId = account.Id;
			offlineMember.EmailAddress = normalizedEmail;
			offlineMember.IsOfflineMember = false;
			await context.Members.SaveAsync(offlineMember, cancellationToken);
			return offlineMember;
		}

		foreach (var allocation in context.Allocations.Values.Where(allocation => allocation.LeagueMemberId == offlineMember.Id))
		{
			allocation.LeagueMemberId = existingMember.Id;
			await context.Allocations.SaveAsync(allocation, cancellationToken);
		}

		foreach (var submission in context.Submissions.Values.Where(submission => submission.LeagueMemberId == offlineMember.Id))
		{
			submission.LeagueMemberId = existingMember.Id;
			await context.Submissions.SaveAsync(submission, cancellationToken);
		}

		foreach (var challenge in context.Challenges.Values.Where(challenge => challenge.LeagueId == leagueId))
		{
			ReplaceMemberId(challenge.TargetMemberIds, offlineMember.Id, existingMember.Id);
			ReplaceMemberId(challenge.AcceptedMemberIds, offlineMember.Id, existingMember.Id);
			ReplaceMemberId(challenge.RejectedMemberIds, offlineMember.Id, existingMember.Id);
			ReplaceMemberId(challenge.CompletedMemberIds, offlineMember.Id, existingMember.Id);
			ReplaceMemberId(challenge.FailedMemberIds, offlineMember.Id, existingMember.Id);
			await context.Challenges.SaveAsync(challenge, cancellationToken);
		}

		offlineMember.Status = LeagueMemberStatus.Removed;
		offlineMember.EmailAddress = normalizedEmail;
		await context.Members.SaveAsync(offlineMember, cancellationToken);
		return existingMember;
	}

	private string ValidateDisplayName(Guid leagueId, string displayName, Guid? excludedMemberId = null)
	{
		if (string.IsNullOrWhiteSpace(displayName))
		{
			throw new InvalidOperationException("Display name is required.");
		}

		var normalized = displayName.Trim();
		if (context.Members.Values.Any(member =>
			member.LeagueId == leagueId &&
			member.Id != excludedMemberId &&
			member.Status != LeagueMemberStatus.Removed &&
			string.Equals(member.DisplayName, normalized, StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidOperationException("Display name is already in use in this league.");
		}

		return normalized;
	}

	private LeagueMember GetLeagueMember(Guid leagueId, Guid memberId)
	{
		if (!context.Members.TryGetValue(memberId, out var member) || member.LeagueId != leagueId || member.Status == LeagueMemberStatus.Removed)
		{
			throw new InvalidOperationException("League member was not found.");
		}

		return member;
	}

	private async Task EnsureCanSetRoleAsync(Guid leagueId, Guid userId, LeagueMember adminMembership, LeagueMember member, LeagueMemberRole role, CancellationToken cancellationToken)
	{
		if (role == LeagueMemberRole.Owner || member.Role == LeagueMemberRole.Owner)
		{
			await authorisationService.GetRequiredOwnerMembershipAsync(leagueId, userId, cancellationToken);
		}

		if (member.Role == LeagueMemberRole.Owner && role != LeagueMemberRole.Owner)
		{
			EnsureAnotherOwnerRemains(leagueId);
		}

		if (adminMembership.Role != LeagueMemberRole.Owner && member.Role == LeagueMemberRole.Admin && role != LeagueMemberRole.Admin)
		{
			throw new UnauthorizedAccessException("Only owners can demote admins.");
		}
	}

	private void EnsureAnotherOwnerRemains(Guid leagueId)
	{
		var ownerCount = context.Members.Values.Count(candidate => candidate.LeagueId == leagueId && candidate.Status == LeagueMemberStatus.Active && candidate.Role == LeagueMemberRole.Owner);
		if (ownerCount <= 1)
		{
			throw new InvalidOperationException("There must always be at least one owner.");
		}
	}

	private static string? NormalizeOptionalEmail(string? emailAddress)
	{
		return string.IsNullOrWhiteSpace(emailAddress)
			? null
			: UserService.NormalizeEmail(emailAddress);
	}

	private static void ReplaceMemberId(List<Guid> memberIds, Guid fromMemberId, Guid toMemberId)
	{
		if (!memberIds.Remove(fromMemberId) || memberIds.Contains(toMemberId))
		{
			return;
		}

		memberIds.Add(toMemberId);
	}
}

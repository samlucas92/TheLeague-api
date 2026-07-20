using System.Collections.Concurrent;
using TheLeague.Models;

namespace TheLeague.Mongo.Context;

public class LeagueDataContext
{
	public ConcurrentDictionary<Guid, UserAccount> Users { get; } = new();
	public ConcurrentDictionary<Guid, League> Leagues { get; } = new();
	public ConcurrentDictionary<Guid, LeagueMember> Members { get; } = new();
	public ConcurrentDictionary<Guid, Challenge> Challenges { get; } = new();
	public ConcurrentDictionary<Guid, PointSubmission> Submissions { get; } = new();
	public ConcurrentDictionary<Guid, PointAllocation> Allocations { get; } = new();
	public ConcurrentDictionary<Guid, LeagueAuditEntry> AuditEntries { get; } = new();
}

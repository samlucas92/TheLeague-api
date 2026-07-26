using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using TheLeague.Models;

namespace TheLeague.Mongo.Context;

public class LeagueDataContext
{
	static LeagueDataContext()
	{
		try
		{
			BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
		}
		catch (BsonSerializationException)
		{
			// The serializer may already be registered in test hosts or warm app restarts.
		}
	}

	public LeagueDataContext() : this(Options.Create(new MongoSettings()))
	{
	}

	public LeagueDataContext(IOptions<MongoSettings> options)
	{
		var settings = options.Value;
		if (string.IsNullOrWhiteSpace(settings.ConnectionString))
		{
			Users = MongoEntitySet<UserAccount>.InMemory();
			Leagues = MongoEntitySet<League>.InMemory();
			Members = MongoEntitySet<LeagueMember>.InMemory();
			Challenges = MongoEntitySet<Challenge>.InMemory();
			Submissions = MongoEntitySet<PointSubmission>.InMemory();
			Allocations = MongoEntitySet<PointAllocation>.InMemory();
			AuditEntries = MongoEntitySet<LeagueAuditEntry>.InMemory();
			PasswordResetTokens = MongoEntitySet<PasswordResetToken>.InMemory();
			EmailMessages = MongoEntitySet<EmailMessage>.InMemory();
			return;
		}

		var databaseName = string.IsNullOrWhiteSpace(settings.DatabaseName) ? "the-league" : settings.DatabaseName;
		var database = new MongoClient(settings.ConnectionString).GetDatabase(databaseName);
		Users = MongoEntitySet<UserAccount>.FromCollection(database.GetCollection<UserAccount>("users"));
		Leagues = MongoEntitySet<League>.FromCollection(database.GetCollection<League>("leagues"));
		Members = MongoEntitySet<LeagueMember>.FromCollection(database.GetCollection<LeagueMember>("members"));
		Challenges = MongoEntitySet<Challenge>.FromCollection(database.GetCollection<Challenge>("challenges"));
		Submissions = MongoEntitySet<PointSubmission>.FromCollection(database.GetCollection<PointSubmission>("submissions"));
		Allocations = MongoEntitySet<PointAllocation>.FromCollection(database.GetCollection<PointAllocation>("allocations"));
		AuditEntries = MongoEntitySet<LeagueAuditEntry>.FromCollection(database.GetCollection<LeagueAuditEntry>("auditEntries"));
		PasswordResetTokens = MongoEntitySet<PasswordResetToken>.FromCollection(database.GetCollection<PasswordResetToken>("passwordResetTokens"));
		EmailMessages = MongoEntitySet<EmailMessage>.FromCollection(database.GetCollection<EmailMessage>("emailMessages"));
		CreateIndexes(database);
	}

	public MongoEntitySet<UserAccount> Users { get; }
	public MongoEntitySet<League> Leagues { get; }
	public MongoEntitySet<LeagueMember> Members { get; }
	public MongoEntitySet<Challenge> Challenges { get; }
	public MongoEntitySet<PointSubmission> Submissions { get; }
	public MongoEntitySet<PointAllocation> Allocations { get; }
	public MongoEntitySet<LeagueAuditEntry> AuditEntries { get; }
	public MongoEntitySet<PasswordResetToken> PasswordResetTokens { get; }
	public MongoEntitySet<EmailMessage> EmailMessages { get; }

	private static void CreateIndexes(IMongoDatabase database)
	{
		CreateIndex(database.GetCollection<UserAccount>("users"), Builders<UserAccount>.IndexKeys.Ascending(user => user.EmailAddress), "ux_users_email", unique: true);
		CreateIndex(database.GetCollection<League>("leagues"), Builders<League>.IndexKeys.Ascending(league => league.JoinCode), "ux_leagues_join_code", unique: true);
	}

	private static void CreateIndex<T>(IMongoCollection<T> collection, IndexKeysDefinition<T> keys, string name, bool unique) where T : class
	{
		try
		{
			collection.Indexes.CreateOne(new CreateIndexModel<T>(keys, new CreateIndexOptions { Name = name, Unique = unique }));
		}
		catch (MongoCommandException exception) when (exception.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict" or "DuplicateKey" || exception.Code == 11000)
		{
			// Keep booting if Atlas already has indexes or duplicate seed data from an earlier deploy.
		}
	}
}

public class MongoEntitySet<T> where T : class
{
	private static readonly PropertyInfo IdProperty = typeof(T).GetProperty("Id")
		?? throw new InvalidOperationException($"{typeof(T).Name} must have an Id property.");

	private readonly ConcurrentDictionary<Guid, T> items = new();
	private readonly IMongoCollection<T>? collection;

	private MongoEntitySet(IMongoCollection<T>? collection)
	{
		this.collection = collection;
		if (collection is null)
		{
			return;
		}

		foreach (var item in collection.Find(Builders<T>.Filter.Empty).ToList())
		{
			items[GetId(item)] = item;
		}
	}

	public IEnumerable<T> Values => items.Values;

	public T this[Guid id]
	{
		get => items[id];
		set
		{
			items[id] = value;
			SaveAsync(value).GetAwaiter().GetResult();
		}
	}

	public static MongoEntitySet<T> InMemory() => new(null);

	public static MongoEntitySet<T> FromCollection(IMongoCollection<T> collection) => new(collection);

	public bool TryGetValue(Guid id, [MaybeNullWhen(false)] out T item) => items.TryGetValue(id, out item);

	public async Task SaveAsync(T item, CancellationToken cancellationToken = default)
	{
		var id = GetId(item);
		items[id] = item;
		if (collection is null)
		{
			return;
		}

		await collection.ReplaceOneAsync(
			Builders<T>.Filter.Eq("_id", id),
			item,
			new ReplaceOptions { IsUpsert = true },
			cancellationToken);
	}

	public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var removed = items.TryRemove(id, out _);
		if (collection is null)
		{
			return removed;
		}

		var result = await collection.DeleteOneAsync(Builders<T>.Filter.Eq("_id", id), cancellationToken);
		return removed || result.DeletedCount > 0;
	}

	private static Guid GetId(T item)
	{
		if (IdProperty.GetValue(item) is not Guid id || id == Guid.Empty)
		{
			throw new InvalidOperationException($"{typeof(T).Name} must have a non-empty Id.");
		}

		return id;
	}
}

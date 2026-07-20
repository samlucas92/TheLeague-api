namespace TheLeague.Tests;

public class ArchitectureTests
{
	[Test]
	public void CoreProjectDoesNotReferenceInfrastructureOrWeb()
	{
		var references = typeof(TheLeague.Models.League).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

		Assert.That(references, Does.Not.Contain("TheLeague.Mongo"));
		Assert.That(references, Does.Not.Contain("TheLeague.Web"));
	}

	[Test]
	public void MongoProjectDoesNotReferenceWeb()
	{
		var references = typeof(TheLeague.Mongo.Context.LeagueDataContext).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

		Assert.That(references, Does.Not.Contain("TheLeague.Web"));
	}
}

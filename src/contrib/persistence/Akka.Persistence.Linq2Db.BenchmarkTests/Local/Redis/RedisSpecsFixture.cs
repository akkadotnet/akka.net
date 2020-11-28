using Xunit;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.Redis
{
    [CollectionDefinition("RedisSpec")]
    public sealed class RedisSpecsFixture : ICollectionFixture<RedisFixture>
    {
    }
}
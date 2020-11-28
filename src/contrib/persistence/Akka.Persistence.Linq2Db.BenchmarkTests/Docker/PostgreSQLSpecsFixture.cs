using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using Xunit;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Docker.Linq2Db
{
    [CollectionDefinition("PostgreSQLSpec")]
    public sealed class PostgreSQLSpecsFixture : ICollectionFixture<PostgreSQLFixture>
    {
    }
}
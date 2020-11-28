using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using Xunit;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    [CollectionDefinition("PostgreSQLSpec")]
    public sealed class PostgreSQLSpecsFixture : ICollectionFixture<PostgreSQLFixture>
    {
    }
}
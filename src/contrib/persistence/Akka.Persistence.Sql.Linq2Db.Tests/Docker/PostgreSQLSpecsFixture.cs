using Xunit;

namespace Akka.Persistence.Sql.Linq2Db.Tests.Docker
{
    [CollectionDefinition("PostgreSQLSpec")]
    public sealed class PostgreSQLSpecsFixture : ICollectionFixture<PostgreSQLFixture>
    {
    }
}
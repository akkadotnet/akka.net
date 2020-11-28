using Xunit;

namespace Akka.Persistence.Sql.Linq2Db.Tests.Docker
{
    [CollectionDefinition("SqlServerSpec")]
    public sealed class SqlServerSpecsFixture : ICollectionFixture<SqlServerFixture>
    {
    }
}
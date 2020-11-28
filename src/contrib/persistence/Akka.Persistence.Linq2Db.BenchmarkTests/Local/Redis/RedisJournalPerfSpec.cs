using Akka.Configuration;
using Akka.Persistence.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.Redis
{
    [Collection("RedisSpec")]
    public class RedisJournalPerfSpec : L2dbJournalPerfSpec
    {
        public const int Database = 1;

        public static Config Config(RedisFixture fixture, int id)
        {
            RedisDbUtils.Initialize(fixture);

            return ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                #plugin-dispatcher = ""akka.actor.default-dispatcher""
                plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""                
                configuration-string = ""{fixture.ConnectionString}""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
                .WithFallback(RedisPersistence.DefaultConfig())
                .WithFallback(Persistence.DefaultConfig());
        }

        public RedisJournalPerfSpec(ITestOutputHelper output, RedisFixture fixture) : base(Config(fixture, Database), nameof(RedisJournalPerfSpec), output, eventsCount: TestConstants.NumMessages)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            RedisDbUtils.Clean(Database);
        }
    }
}
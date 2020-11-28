using System.Linq;
using StackExchange.Redis;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.Redis
{
    public static class RedisDbUtils
    {
        public static string ConnectionString { get; private set; }

        public static void Initialize(RedisFixture fixture)
        {
            ConnectionString = fixture._redis.Endpoint.ToString();
        }

        public static void Clean(int database)
        {
            var connectionString = $"{ConnectionString},allowAdmin=true";

            var redisConnection = ConnectionMultiplexer.Connect(connectionString);
            var server = redisConnection.GetServer(redisConnection.GetEndPoints().First());
            server.FlushDatabase(database);
        }
    }
}
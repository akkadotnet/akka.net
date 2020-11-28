using System.Threading.Tasks;
using Xunit;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.Redis
{
    public class RedisFixture : IAsyncLifetime
    { 
        public string ConnectionString { get; private set; }
        public RedisInside.Redis _redis;
        public Task InitializeAsync()
        {
            _redis = new RedisInside.Redis((config =>config.Port(9001) ));
            ConnectionString = $"localhost:{9001}";
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            _redis.Dispose();
            return Task.CompletedTask;
        }
    }
}
using Akka.IO;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Tests.IO
{
    public class SimpleDnsCacheSpec
    {
        private class SimpleDnsCacheTestDouble : SimpleDnsCache
        {
            private readonly AtomicReference<long> _clock;

            public SimpleDnsCacheTestDouble(AtomicReference<long> clock)
            {
                _clock = clock;
            }

            protected override long Clock()
            {
                return _clock.Value;
            }
        }

        [Fact]
        public void Cache_should_not_reply_with_expired_but_not_yet_swept_out_entries()
        {
            var localClock = new AtomicReference<long>(0);
            var cache = new SimpleDnsCacheTestDouble(localClock);
            var cacheEntry = Dns.Resolved.Create("test.local", System.Net.Dns.GetHostEntry("127.0.0.1").AddressList);
            cache.Put(cacheEntry, 5000);

            cache.Cached("test.local").ShouldBe(cacheEntry);
            localClock.CompareAndSet(0, 4999);
            cache.Cached("test.local").ShouldBe(cacheEntry);
            localClock.CompareAndSet(4999, 5000);
            cache.Cached("test.local").ShouldBe(null);

        }

        [Fact]
        public void Cache_should_sweep_out_expired_entries_on_cleanup()
        {
            var localClock = new AtomicReference<long>(0);
            var cache = new SimpleDnsCacheTestDouble(localClock);
            var cacheEntry = Dns.Resolved.Create("test.local", System.Net.Dns.GetHostEntry("127.0.0.1").AddressList);
            cache.Put(cacheEntry, 5000);

            cache.Cached("test.local").ShouldBe(cacheEntry);
            localClock.CompareAndSet(0, 5000);
            cache.Cached("test.local").ShouldBe(null);
            localClock.CompareAndSet(5000, 0);
            cache.Cached("test.local").ShouldBe(cacheEntry);
            localClock.CompareAndSet(0, 5000);
            cache.CleanUp();
            cache.Cached("test.local").ShouldBe(null);
            localClock.CompareAndSet(5000, 0);
            cache.Cached("test.local").ShouldBe(null);
           
        }
    }
}

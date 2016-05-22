using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Remote.Tests
{
    public class ActorRefCacheSpec : AkkaSpec
    {
        private static Config _config = ConfigurationFactory.ParseString(@"
akka.scheduler.implementation = ""Akka.TestKit.TestScheduler, Akka.TestKit""

akka.remote {
    actorref-cache {
        enabled = true
        maximum-remote-cache-size = 4
        remote-cache-expiration = 1s
    }
}
");
        private ActorRefCache Cache { get; set; }

        private IActorRef CacheActor { get; set; }

        public TestScheduler Scheduler
        {
            get { return (TestScheduler)Sys.Scheduler; }
        }

        public ActorRefCacheSpec()
            : base(_config)
        { }

        protected override void AtStartup()
        {
            base.AtStartup();

            Cache = ActorRefCache.Create((ExtendedActorSystem)Sys);
            CacheActor = Sys.ActorSelection("/system/actorref-cache").ResolveOne(TimeSpan.FromSeconds(1)).Result;
        }

        [Fact]
        public void LocalActorRef_should_be_added_and_cleared()
        {
            var actorRef = (IInternalActorRef)ActorOf<DummyActor>("a");

            string path = actorRef.Path.ToString();

            Cache.Add(path, actorRef);

            AssertLocalCacheSize(1);

            Watch(actorRef);
            actorRef.Tell(PoisonPill.Instance);
            ExpectTerminated(actorRef, TimeSpan.FromSeconds(1));

            AssertLocalCacheSize(0);
        }

        [Fact]
        public void Temp_actors_should_not_be_cached()
        {
            var system = (ExtendedActorSystem)Sys;

            var actorPath = system.Provider.TempPath();
            var actorRef = new DummyActorRef(actorPath);

            string path = actorPath.ToString();

            Cache.Add(path, actorRef);

            AssertLocalCacheSize(0);
        }

        [Fact]
        public void RemoteActorRef_should_be_added_and_expired()
        {
            var actorRef = new DummyActorRef(new RootActorPath(Address.AllSystems) / "remote" / "a");

            string path = actorRef.Path.ToString();

            Cache.Add(path, actorRef);

            AssertRemoteCacheSize(1);

            // Trigger the cache expiration
            AdvanceOneSecond();

            AssertRemoteCacheSize(0);
        }

        [Fact]
        public void Cache_should_be_limited_by_max_size()
        {
            IInternalActorRef actorRef;
            ActorPath basePath = new RootActorPath(Address.AllSystems) / "remote";

            // Fill the cache
            actorRef = new DummyActorRef(basePath / "1");
            Cache.Add(actorRef.Path.ToString(), actorRef);

            actorRef = new DummyActorRef(basePath / "2");
            Cache.Add(actorRef.Path.ToString(), actorRef);

            actorRef = new DummyActorRef(basePath / "3");
            Cache.Add(actorRef.Path.ToString(), actorRef);

            actorRef = new DummyActorRef(basePath / "4");
            Cache.Add(actorRef.Path.ToString(), actorRef);

            AssertRemoteCacheSize(4);

            // Make a cache hit for actor 1, it will move to the front of the LRU
            Assert.True(Cache.TryGetActorRef((basePath / "1").ToString(), out actorRef));

            actorRef = new DummyActorRef(basePath / "5");
            Cache.Add(actorRef.Path.ToString(), actorRef);

            // Actor 2 should have been cleared from cache
            AssertRemoteCacheSize(4);

            Assert.False(Cache.TryGetActorRef((basePath / "2").ToString(), out actorRef));

            AdvanceOneSecond();
            AssertRemoteCacheSize(0);
        }

        private void AdvanceOneSecond()
        {
            Scheduler.Advance(TimeSpan.FromSeconds(1));
        }

        private void AssertLocalCacheSize(int expected)
        {
            var cacheSize = GetCacheSize();

            Assert.Equal(expected, cacheSize.LocalActorRefCount);
        }

        private void AssertRemoteCacheSize(int expected)
        {
            var cacheSize = GetCacheSize();

            Assert.Equal(expected, cacheSize.RemoteActorRefCount);
        }

        private ActorRefCacheController.CacheSize GetCacheSize()
        {
            return CacheActor.Ask<ActorRefCacheController.CacheSize>(new ActorRefCacheController.GetCacheSize(), TimeSpan.FromSeconds(1)).Result;
        }
    }

    public class DummyActor : ReceiveActor
    { }

    public class DummyActorRef : MinimalActorRef
    {

        public override ActorPath Path { get; }

        public override IActorRefProvider Provider
        {
            get { return null; }
        }

        public DummyActorRef(ActorPath path)
        {
            Path = path;
        }
    }
}
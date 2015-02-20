using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using Akka.Util.Internal;

namespace Akka.Persistence.TestKit
{
    public abstract class PluginSpec : TestKitBase, IDisposable
    {
        private readonly AtomicCounter _counter = new AtomicCounter(0);
        private readonly PersistenceExtension _extension;
        private string _pid;

        protected int ActorInstanceId = 1;

        protected PluginSpec(Config config = null, string actorSystemName = null, string testActorName = null) 
            : base(new XunitAssertions(), FromConfig(config), actorSystemName, testActorName)
        {
            _extension = Persistence.Instance.Apply(Sys as ExtendedActorSystem);
            _pid = "p-" + _counter.IncrementAndGet();
        }

        protected static Config FromConfig(Config config = null)
        {
            return config == null
                ? Persistence.DefaultConfig()
                : config.WithFallback(Persistence.DefaultConfig());
        }

        public string Pid { get { return _pid; } }
        public PersistenceExtension Extension { get { return _extension; } }

        public void Subscribe<T>(ActorRef subscriber)
        {
            Sys.EventStream.Subscribe(subscriber, typeof (T));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                Shutdown();
        }
    }
}
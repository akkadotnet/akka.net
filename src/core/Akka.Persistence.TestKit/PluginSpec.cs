using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;

namespace Akka.Persistence.TestKit
{
    public abstract class PluginSpec : TestKitBase, IDisposable
    {
        private readonly AtomicCounter _counter = new AtomicCounter(0);
        private PersistenceExtension _extension;
        private string _pid;

        protected PluginSpec(TestKitAssertions assertions, ActorSystem system = null, string testActorName = null) 
            : base(assertions, system, testActorName)
        {
            Init();
        }

        protected PluginSpec(TestKitAssertions assertions, Config config, string actorSystemName = null, string testActorName = null) 
            : base(assertions, config.WithFallback(Persistence.DefaultConfig()), actorSystemName, testActorName)
        {
            Init();
        }

        private void Init()
        {
            _extension = Persistence.Instance.Apply(Sys as ExtendedActorSystem);
            _pid = "p-" + _counter.IncrementAndGet();
        }

        public string Pid { get { return _pid; } }
        public PersistenceExtension Extension { get { return _extension; } }

        public void Subscribe<T>(ActorRef subscriber)
        {
            Sys.EventStream.Subscribe(subscriber, typeof (T));
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
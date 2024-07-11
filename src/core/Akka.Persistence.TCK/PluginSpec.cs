//-----------------------------------------------------------------------
// <copyright file="PluginSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK
{
    public abstract class PluginSpec : Akka.TestKit.Xunit2.TestKit, IDisposable
    {
        private static readonly AtomicCounter Counter = new(0);

        protected int ActorInstanceId = 1;

        protected PluginSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null) 
            : base(FromConfig(config), actorSystemName, output)
        {
            Extension = Persistence.Instance.Apply(Sys as ExtendedActorSystem);
            Pid = "p-" + Counter.IncrementAndGet();
            WriterGuid = Guid.NewGuid().ToString();
        }

        protected PluginSpec(ActorSystemSetup setup, string actorSystemName = null, ITestOutputHelper output = null)
            : base(setup, actorSystemName, output)
        {
            Extension = Persistence.Instance.Apply(Sys as ExtendedActorSystem);
            Pid = "p-" + Counter.IncrementAndGet();
            WriterGuid = Guid.NewGuid().ToString();
        }

        protected PluginSpec(ActorSystem system = null, ITestOutputHelper output = null)
            : base(system, output)
        {
            Extension = Persistence.Instance.Apply(Sys as ExtendedActorSystem);
            Pid = "p-" + Counter.IncrementAndGet();
            WriterGuid = Guid.NewGuid().ToString();
        }

        protected static Config FromConfig(Config config = null)
        {
            return config.IsNullOrEmpty()
                ? Persistence.DefaultConfig()
                : config.WithFallback(Persistence.DefaultConfig());
        }

        /// <summary>
        /// When enabled, allows serialization specs to run.
        /// </summary>
        protected virtual bool SupportsSerialization { get; } = false;

        public PersistenceExtension Extension { get; }
        public string Pid { get; }
        public string WriterGuid { get; }

        public void Subscribe<T>(IActorRef subscriber)
        {
            Sys.EventStream.Subscribe(subscriber, typeof (T));
        }
    }
}


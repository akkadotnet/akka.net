//-----------------------------------------------------------------------
// <copyright file="PluginSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.TestKit
{
    public abstract class PluginSpec : Akka.TestKit.Xunit2.TestKit, IDisposable
    {
        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        private readonly PersistenceExtension _extension;
        private string _pid;

        protected int ActorInstanceId = 1;

        protected PluginSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null) 
            : base(FromConfig(config), actorSystemName, output)
        {
            _extension = Persistence.Instance.Apply(Sys as ExtendedActorSystem);
            _pid = "p-" + Counter.IncrementAndGet();
        }

        protected static Config FromConfig(Config config = null)
        {
            return config == null
                ? Persistence.DefaultConfig()
                : config.WithFallback(Persistence.DefaultConfig());
        }

        public string Pid { get { return _pid; } }
        public PersistenceExtension Extension { get { return _extension; } }

        public void Subscribe<T>(IActorRef subscriber)
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
            //if (disposing) FSMBase.Shutdown();
        }
    }
}


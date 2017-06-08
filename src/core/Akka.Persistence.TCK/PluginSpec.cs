//-----------------------------------------------------------------------
// <copyright file="PluginSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK
{
    public abstract class PluginSpec : Akka.TestKit.Xunit2.TestKit, IDisposable
    {
        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        private readonly PersistenceExtension _extension;
        private string _pid;
        private string _writerGuid;

        protected int ActorInstanceId = 1;

        protected PluginSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null) 
            : base(FromConfig(config), actorSystemName, output)
        {
            _extension = Persistence.Instance.Apply(Sys as ExtendedActorSystem);
            _pid = "p-" + Counter.IncrementAndGet();
            _writerGuid = Guid.NewGuid().ToString();
        }

        protected static Config FromConfig(Config config = null)
        {
            return config == null
                ? Persistence.DefaultConfig()
                : config.WithFallback(Persistence.DefaultConfig());
        }

        public PersistenceExtension Extension { get { return _extension; } }
        public string Pid { get { return _pid; } }
        public string WriterGuid { get { return _writerGuid; } }

        public void Subscribe<T>(IActorRef subscriber)
        {
            Sys.EventStream.Subscribe(subscriber, typeof (T));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        /// <param name="disposing">if set to <c>true</c> the method has been called directly or indirectly by a
        /// user's code. Managed and unmanaged resources will be disposed.<br />
        /// if set to <c>false</c> the method has been called by the runtime from inside the finalizer and only
        /// unmanaged resources can be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            //if (disposing) FSMBase.Shutdown();
        }
    }
}


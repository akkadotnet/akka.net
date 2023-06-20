//-----------------------------------------------------------------------
// <copyright file="TestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit.Xunit2.Internals;
using Xunit.Abstractions;

namespace Akka.TestKit.Xunit2
{
    /// <summary>
    /// This class represents an Akka.NET TestKit that uses <a href="https://xunit.github.io/">xUnit</a>
    /// as its testing framework.
    /// </summary>
    public class TestKit : TestKitBase, IDisposable
    {
        private class PrefixedOutput : ITestOutputHelper
        {
            private readonly ITestOutputHelper output;
            private readonly string prefix;

            public PrefixedOutput(ITestOutputHelper output, string prefix)
            {
                this.output = output;
                this.prefix = prefix;
            }

            public void WriteLine(string message)
            {
                output.WriteLine(prefix + message);
            }

            public void WriteLine(string format, params object[] args)
            {
                output.WriteLine(prefix + format, args);
            }
        }

        /// <summary>
        /// The provider used to write test output.
        /// </summary>
        protected readonly ITestOutputHelper Output;

        private bool _disposed;
        private bool _disposing;
        
        /// <summary>
        /// <para>
        /// Initializes a new instance of the <see cref="TestKit"/> class.
        /// </para>
        /// <para>
        /// If no <paramref name="system"/> is passed in, a new system with
        /// <see cref="DefaultConfig"/> will be created.
        /// </para>
        /// </summary>
        /// <param name="system">The actor system to use for testing. The default value is <see langword="null"/>.</param>
        /// <param name="output">The provider used to write test output. The default value is <see langword="null"/>.</param>
        public TestKit(ActorSystem system = null, ITestOutputHelper output = null)
            : base(Assertions, system)
        {
            Output = output;
            InitializeLogger(Sys);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestKit"/> class.
        /// </summary>
        /// <param name="config">The <see cref="Setup"/> to use for configuring the ActorSystem.</param>
        /// <param name="actorSystemName">The name of the system. The default name is "test".</param>
        /// <param name="output">The provider used to write test output. The default value is <see langword="null"/>.</param>
        public TestKit(ActorSystemSetup config, string actorSystemName = null, ITestOutputHelper output = null)
            : base(Assertions, config, actorSystemName)
        {
            Output = output;
            InitializeLogger(Sys);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestKit"/> class.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        /// <param name="actorSystemName">The name of the system. The default name is "test".</param>
        /// <param name="output">The provider used to write test output. The default value is <see langword="null"/>.</param>
        public TestKit(Config config, string actorSystemName = null, ITestOutputHelper output = null)
            : base(Assertions, config, actorSystemName)
        {
            Output = output;
            InitializeLogger(Sys);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestKit"/> class.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        /// <param name="output">The provider used to write test output. The default value is <see langword="null"/>.</param>
        public TestKit(string config, ITestOutputHelper output = null)
            : base(Assertions, ConfigurationFactory.ParseString(config))
        {
            Output = output;
            InitializeLogger(Sys);
        }

        /// <summary>
        /// A configuration that has just the default log settings enabled. The default settings can be found in
        /// <a href="https://github.com/akkadotnet/akka.net/blob/master/src/core/Akka.TestKit/Internal/Reference.conf">Akka.TestKit.Internal.Reference.conf</a>.
        /// </summary>
        public new static Config DefaultConfig => TestKitBase.DefaultConfig;

        /// <summary>
        /// A configuration that has all log settings enabled
        /// </summary>
        public new static Config FullDebugConfig => TestKitBase.FullDebugConfig;

        /// <summary>
        /// Commonly used assertions used throughout the testkit.
        /// </summary>
        protected static XunitAssertions Assertions { get; } = new XunitAssertions();

        /// <summary>
        /// This method is called when a test ends.
        /// </summary>
        protected virtual void AfterAll()
        {
        }

        /// <summary>
        /// Initializes a new <see cref="TestOutputLogger"/> used to log messages.
        /// </summary>
        /// <param name="system">The actor system used to attach the logger</param>
        protected void InitializeLogger(ActorSystem system)
        {
            if (Output != null)
            {
                var extSystem = (ExtendedActorSystem)system;
                var logger = extSystem.SystemActorOf(Props.Create(() => new TestOutputLogger(Output)), "log-test");
                logger.Ask<LoggerInitialized>(new InitializeLogger(system.EventStream), TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }

        protected void InitializeLogger(ActorSystem system, string prefix)
        {
            if (Output != null)
            {
                var extSystem = (ExtendedActorSystem)system;
                var logger = extSystem.SystemActorOf(Props.Create(() => new TestOutputLogger(
                    string.IsNullOrEmpty(prefix) ? Output : new PrefixedOutput(Output, prefix))), "log-test");
                logger.Ask<LoggerInitialized>(new InitializeLogger(system.EventStream), TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <param name="disposing">
        /// if set to <c>true</c> the method has been called directly or indirectly by a  user's code.
        /// Managed and unmanaged resources will be disposed.<br /> if set to <c>false</c> the method
        /// has been called by the runtime from inside the finalizer and only unmanaged resources can
        ///  be disposed.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposing || _disposed)
                return;
            
            _disposing = true;
            try
            {
                AfterAll();
            }
            finally
            {
                Shutdown();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}

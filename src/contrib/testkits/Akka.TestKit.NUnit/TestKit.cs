//-----------------------------------------------------------------------
// <copyright file="TestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using NUnit.Framework;

namespace Akka.TestKit.NUnit
{
    /// <summary>
    /// TestKit for NUnit.
    /// </summary>
    public class TestKit : TestKitBase, IDisposable
    {
        private static readonly NUnitAssertions _assertions = new NUnitAssertions();
        private readonly Config _config;
        private readonly string _actorSystemName;
        private bool _isFirstRun = true;
        private bool _isDisposed; //Automatically initialized to false;

        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for NUnit class.
        /// If no <paramref name="system"/> is passed in, a new system 
        /// with <see cref="DefaultConfig"/> will be created.
        /// </summary>
        /// <param name="system">Optional: The actor system.</param>
        public TestKit(ActorSystem system = null)
            : base(_assertions, system)
        {
            if(system != null)
                throw new NotSupportedException("Due to the way NUnit works, providing an ActorSystem is not supported.  For further details please see https://github.com/akkadotnet/akka.net/pull/1092");
        }

        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for NUnit class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        /// <param name="actorSystemName">Optional: the name of the system. Default: "test"</param>
        public TestKit(Config config, string actorSystemName = null)
            : base(_assertions, config, actorSystemName)
        {
            _config = config;
            _actorSystemName = actorSystemName;
        }


        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for NUnit class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        public TestKit(string config)
            : base(_assertions, ConfigurationFactory.ParseString(config))
        {
            _config = ConfigurationFactory.ParseString(config);
        }

        public new static Config DefaultConfig { get { return TestKitBase.DefaultConfig; } }
        public new static Config FullDebugConfig { get { return TestKitBase.FullDebugConfig; } }

        protected static NUnitAssertions Assertions { get { return _assertions; } }

        /// <summary>
        /// This method is called before each test run, it initializes the test including
        /// creating and setting up the ActorSystem.
        /// </summary>
        [SetUp]
        public void InitializeActorSystemOnSetUp()
        {
            if (!_isFirstRun)
                InitializeTest(null, _config, _actorSystemName, null);
        }

        /// <summary>
        /// This method is called after each test finishes, which calls
        /// into the AfterAll method.
        /// </summary>
        [TearDown]
        public void ShutDownActorSystemOnTearDown()
        {
            _isFirstRun = false;
            AfterAll();
        }

        /// <summary>
        /// This method is called when a test ends. 
        /// <remarks>If you override this, make sure you either call 
        /// base.AfterTest() or <see cref="TestKitBase.Shutdown(System.Nullable{System.TimeSpan},bool)">TestKitBase.Shutdown</see> to shut down
        /// the system. Otherwise you'll leak memory.
        /// </remarks>
        /// </summary>
        protected virtual void AfterAll()
        {
            Shutdown();
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
            Dispose(true);
            //Take this object off the finalization queue and prevent finalization code for this object
            //from executing a second time.
            GC.SuppressFinalize(this);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        /// <param name="disposing">if set to <c>true</c> the method has been called directly or indirectly by a 
        /// user's code. Managed and unmanaged resources will be disposed.<br />
        /// if set to <c>false</c> the method has been called by the runtime from inside the finalizer and only 
        /// unmanaged resources can be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            // If disposing equals false, the method has been called by the
            // runtime from inside the finalizer and you should not reference
            // other objects. Only unmanaged resources can be disposed.

            //Make sure Dispose does not get called more than once, by checking the disposed field
            if (!_isDisposed)
            {
                if (disposing)
                {
                    AfterAll();
                }
            }
            _isDisposed = true;
        }
    }
}

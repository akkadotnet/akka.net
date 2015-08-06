//-----------------------------------------------------------------------
// <copyright file="TestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Machine.Specifications;

namespace Akka.TestKit.MSpec
{
    /// <summary>
    /// TestKit for NUnit.
    /// </summary>
    public class TestKit : TestKitBase
    {
        protected static TestKit Kit;

        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for MSpec class.
        /// If no <paramref name="system"/> is passed in, a new system 
        /// with <see cref="DefaultConfig"/> will be created.
        /// </summary>
        /// <param name="system">Optional: The actor system.</param>
        public TestKit(ActorSystem system = null)
            : base(_assertions, system)
        {
            Kit = this;
        }

        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for MSpec class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        /// <param name="actorSystemName">Optional: the name of the system. Default: "test"</param>
        public TestKit(Config config, string actorSystemName = null)
            : base(_assertions, config, actorSystemName)
        {
            Kit = this;
        }


        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for MSpec class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        public TestKit(string config)
            : base(_assertions, ConfigurationFactory.ParseString(config))
        {
            Kit = this;
        }

        private Cleanup _cleanup = () => Kit.Shutdown();

        private static readonly MSpecAssertions _assertions = new MSpecAssertions();
        protected static MSpecAssertions Assertions { get { return _assertions; } }
    }
}

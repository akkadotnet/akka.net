//-----------------------------------------------------------------------
// <copyright file="TestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;

namespace Akka.TestKit.VsTest
{
    /// <summary>
    /// TestKit for Visual Studio Unit Testing Framework.
    /// You should always call Shutdown from your cleanup method, in order to not leak memory.
    /// Example:
    /// <code>
    ///     [TestClass]
    ///     public class MyTests : TestKit
    ///     {
    ///         [TestCleanup]
    ///         public void Cleanup()
    ///         {
    ///             Shutdown();
    ///         }
    ///     
    ///         [TestMethod]
    ///         public void Expect_a_message()
    ///         {
    ///             TestActor.Tell("Test");
    ///             ExpectMsg("Test");
    ///         }
    ///     }
    /// </code>
    /// </summary>
    public class TestKit : TestKitBase
    {
        private static readonly VsTestAssertions _assertions = new VsTestAssertions();

        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for xUnit class.
        /// If no <paramref name="system"/> is passed in, a new system 
        /// with <see cref="DefaultConfig"/> will be created.
        /// </summary>
        /// <param name="system">Optional: The actor system.</param>
        public TestKit(ActorSystem system = null)
            : base(_assertions, system)
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for xUnit class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        /// <param name="actorSystemName">Optional: the name of the system. Default: "test"</param>
        public TestKit(Config config, string actorSystemName=null)
            : base(_assertions, config, actorSystemName)
        {
            //Intentionally left blank
        }


        /// <summary>
        /// Create a new instance of the <see cref="TestKit"/> for xUnit class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        public TestKit(string config): base(_assertions, ConfigurationFactory.ParseString(config))
        {
            //Intentionally left blank
        }

        public new static Config DefaultConfig { get { return TestKitBase.DefaultConfig; } }
        public new static Config FullDebugConfig { get { return TestKitBase.FullDebugConfig; } }

        protected static VsTestAssertions Assertions { get { return _assertions; } }


    }
}


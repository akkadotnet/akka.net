//-----------------------------------------------------------------------
// <copyright file="TestKitAssertionsExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public class TestKitAssertionsExtension : ExtensionIdProvider<TestKitAssertionsProvider>
    {
        private readonly ITestKitAssertions _assertions;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="assertions">TBD</param>
        public TestKitAssertionsExtension(ITestKitAssertions assertions)
        {
            _assertions = assertions;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override TestKitAssertionsProvider CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitAssertionsProvider(_assertions);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static TestKitAssertionsProvider For(ActorSystem system)
        {
            return system.GetExtension<TestKitAssertionsProvider>();
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="TestKitAssertionsExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Provides the test assertion implementation to an actor system through an extension.
    /// </summary>
    public class TestKitAssertionsExtension : ExtensionIdProvider<TestKitAssertionsProvider>
    {
        private readonly ITestKitAssertions _assertions;

        /// <summary>
        /// Creates an extension provider with the test-framework assertion adapter.
        /// </summary>
        /// <param name="assertions">The assertion implementation supplied by the test framework.</param>
        public TestKitAssertionsExtension(ITestKitAssertions assertions)
        {
            _assertions = assertions;
        }

        /// <summary>
        /// Creates the assertion provider for an actor system.
        /// </summary>
        /// <param name="system">The actor system that owns the extension.</param>
        /// <returns>A provider containing the assertion implementation configured for this extension.</returns>
        public override TestKitAssertionsProvider CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitAssertionsProvider(_assertions);
        }

        /// <summary>
        /// Gets the assertion provider installed in an actor system.
        /// </summary>
        /// <param name="system">The actor system whose provider is requested.</param>
        /// <returns>The actor system's test assertion provider.</returns>
        public static TestKitAssertionsProvider For(ActorSystem system)
        {
            return system.GetExtension<TestKitAssertionsProvider>();
        }
    }
}

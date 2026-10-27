//-----------------------------------------------------------------------
// <copyright file="TestKitExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// A extension to be used together with the TestKit.
    /// <example>
    /// To get the settings:
    /// <code>var testKitSettings = TestKitExtension.For(system);</code>
    /// </example>
    /// </summary>
    public class TestKitExtension : ExtensionIdProvider<TestKitSettings>
    {
        /// <summary>
        /// Creates test kit settings from the actor system configuration.
        /// </summary>
        /// <param name="system">The actor system whose configuration supplies the test kit settings.</param>
        /// <returns>The settings parsed from the actor system configuration.</returns>
        public override TestKitSettings CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitSettings(system.Settings.Config);
        }

        /// <summary>
        /// Gets the test kit settings installed in an actor system.
        /// </summary>
        /// <param name="system">The actor system whose settings are requested.</param>
        /// <returns>The actor system's test kit settings.</returns>
        public static TestKitSettings For(ActorSystem system)
        {
            return system.GetExtension<TestKitSettings>();
        }
    }
}

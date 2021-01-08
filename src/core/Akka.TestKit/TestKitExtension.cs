//-----------------------------------------------------------------------
// <copyright file="TestKitExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override TestKitSettings CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitSettings(system.Settings.Config);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static TestKitSettings For(ActorSystem system)
        {
            return system.GetExtension<TestKitSettings>();
        }
    }
}

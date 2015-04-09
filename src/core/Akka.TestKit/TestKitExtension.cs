//-----------------------------------------------------------------------
// <copyright file="TestKitExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public override TestKitSettings CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitSettings(system.Settings.Config);
        }

        public static TestKitSettings For(ActorSystem system)
        {
            return system.GetExtension<TestKitSettings>();
        }
    }
}


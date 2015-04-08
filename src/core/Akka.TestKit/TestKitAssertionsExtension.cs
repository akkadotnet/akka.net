//-----------------------------------------------------------------------
// <copyright file="TestKitAssertionsExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    public class TestKitAssertionsExtension : ExtensionIdProvider<TestKitAssertionsProvider>
    {
        private readonly ITestKitAssertions _assertions;

        public TestKitAssertionsExtension(ITestKitAssertions assertions)
        {
            _assertions = assertions;
        }

        public override TestKitAssertionsProvider CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitAssertionsProvider(_assertions);
        }

        public static TestKitAssertionsProvider For(ActorSystem system)
        {
            return system.GetExtension<TestKitAssertionsProvider>();
        }
    }
}

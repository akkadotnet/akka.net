//-----------------------------------------------------------------------
// <copyright file="FakeV2PortSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.Serialization;
using Akka.TestKit;
using Akka.Tests.Util;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit
{
    /// <summary>
    /// The kit run against a fake legacy and V2 serializer pair: the way a real port's spec looks, and the proof that
    /// <see cref="V2PortSpec"/>'s tests pass on a port that keeps the contract. The golden files under
    /// <c>GoldenBytes/Fake</c> were captured with <c>AKKA_GOLDEN_CAPTURE=all</c>.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public sealed class FakeV2PortSpec : V2PortSpec
    {
        private static readonly GoldenBytes Legacy = GoldenBytes.For("GoldenBytes/Fake", GoldenKind.Legacy);
        private static readonly GoldenBytes V2 = GoldenBytes.For("GoldenBytes/Fake", GoldenKind.V2);

        public FakeV2PortSpec(ITestOutputHelper output) : base(FakePort.Setup(), output)
        {
        }

        protected override GoldenBytes LegacyGolden => Legacy;

        protected override GoldenBytes V2Golden => V2;

        protected override V2Port CreatePort(ExtendedActorSystem system) => FakePort.Port();
    }
}

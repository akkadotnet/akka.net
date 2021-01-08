//-----------------------------------------------------------------------
// <copyright file="ClusterGenerators.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if FSCHECK
using System;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.Annotations;
using Akka.Tests.Shared.Internals.Helpers;
using FsCheck;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// FsCheck data generators for Akka.Cluster types.
    /// </summary>
    [InternalApi]
    public class ClusterGenerators
    {
        public static Arbitrary<Address> AddressGenerator()
        {
            /*
             * In order to help guarantee collisions and duplicates in random tests, we hold all parts
             * of the address other than the port number constant.
             */
            Func<IPAddress, int, Address> combiner = (address, i) => new Address("akka.tcp", "cluster", address.ToString(), i);
            var producer = FsharpDelegateHelper.Create(combiner);

            return Arb.From(Gen.Map2(producer, Arb.Default.IPAddress().Generator, Gen.Choose(1, 65535)));
        }

        public static Arbitrary<UniqueAddress> UniqueAddressGenerator()
        {
            var gen1 = Arb.Default.Int32().Generator;
            var gen2 = AddressGenerator();

            // randomize both addresses and ports
            Func<int, Address, UniqueAddress> combiner = (uid, addr) => new UniqueAddress(addr, uid);
            var producer = FsharpDelegateHelper.Create(combiner);

            return Arb.From(Gen.Map2(producer, gen1, gen2.Generator));
        }

        public static Arbitrary<MemberStatus> MemberStatusGenerator()
        {
            return Arb.From(Gen.Elements(Enum.GetValues(typeof(MemberStatus)).Cast<MemberStatus>()));
        }
    }
}
#endif

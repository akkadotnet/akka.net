using System;
using System.Linq;
using Akka.Actor;
using Akka.Tests.Shared.Internals.Helpers;
using FsCheck;

namespace Akka.Cluster.Tests
{
    public class ClusterGenerators
    {
        public static Arbitrary<Address> AddressGenerator()
        {
            /*
             * In order to help guarantee collisions and duplicates in random tests, we hold all parts
             * of the address other than the port number constant.
             */
            return Arb.From(Gen.Choose(1, 65535).Select(x => new Address("akka.tcp", "cluster", "127.0.0.1", x)));
        }

        public static Arbitrary<UniqueAddress> UniqueAddressGenerator()
        {
            var gen1 = Arb.Default.Int32().Generator;
            var gen2 = AddressGenerator();

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
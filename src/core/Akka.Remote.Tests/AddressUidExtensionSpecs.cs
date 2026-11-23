//-----------------------------------------------------------------------
// <copyright file="AddressUidExtensionSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests
{
    public class AddressUidExtensionSpecs : AkkaSpec
    {
        /// <summary>
        /// Guarantees that the <see cref="AddressUidExtension"/> is thread-safe at startup.
        /// </summary>
        [Fact]
        public void AddressUidExtension_should_always_report_same_value()
        {
            var values = new ConcurrentBag<long>();
            var parallelOps = 1000;
            var loop = Parallel.For(0, parallelOps, _ =>
            {
                values.Add(AddressUidExtension.Uid(Sys));
            });
            SpinWait.SpinUntil(() => loop.IsCompleted);
            Assert.True(values.All(x => x == AddressUidExtension.Uid(Sys)));
        }

        [Fact]
        public void AddressUidExtension_should_report_different_values_for_different_ActorSystems()
        {
            var sys2 = ActorSystem.Create("Sys2");
            try
            {
                var uid1 = AddressUidExtension.Uid(Sys);
                var uid2 = AddressUidExtension.Uid(sys2);
                Assert.NotEqual(uid1, uid2);
            }
            finally
            {
                Shutdown(sys2);
            }
        }

        /// <summary>
        /// Classic DotNetty remoting (the default transport) must keep generating legacy 31-bit uids
        /// so v1.5 nodes can still interoperate.
        /// </summary>
        [Fact]
        public void AddressUidExtension_should_use_the_31bit_generator_for_classic_remoting()
        {
            var addressUid = Sys.WithExtension<AddressUid, AddressUidExtension>();
            Assert.False(addressUid.Uses64BitGenerator);
            Assert.InRange(addressUid.Uid, 1L, int.MaxValue);
        }

        /// <summary>
        /// When Artery is the transport the uid always comes from the full 64-bit generator. Asserts on the
        /// generator choice rather than the drawn value, which could by chance land in the int range.
        /// </summary>
        [Fact]
        public void AddressUidExtension_should_use_the_64bit_generator_when_artery_is_enabled()
        {
            var sys2 = ActorSystem.Create("AddressUidExtensionSpecsArtery",
                ConfigurationFactory.ParseString(@"
                    akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                    akka.remote.artery.enabled = on
                    akka.remote.artery.canonical.hostname = ""127.0.0.1""
                    akka.remote.artery.canonical.port = 0"));
            try
            {
                var addressUid = sys2.WithExtension<AddressUid, AddressUidExtension>();
                Assert.True(addressUid.Uses64BitGenerator);
                Assert.NotEqual(0, addressUid.Uid);
            }
            finally
            {
                Shutdown(sys2);
            }
        }

        [Fact]
        public void AddressUid_31bit_generation_should_stay_within_1_and_int_MaxValue()
        {
            for (var i = 0; i < 1000; i++)
            {
                Assert.InRange(new AddressUid(false).Uid, 1L, int.MaxValue);
            }
        }

        /// <summary>
        /// Statistical check that <see cref="AddressUid"/>'s full-range 64-bit generator actually
        /// draws outside the legacy <c>[1, int.MaxValue]</c> range: each draw lands in that legacy range with
        /// probability ~= 2^31 / 2^64 ~= 1.16e-10, so across 64 draws seeing zero out-of-range values is
        /// effectively impossible unless the generator is broken (e.g. silently clamped back to int range).
        /// </summary>
        [Fact]
        public void AddressUid_64bit_generation_should_draw_at_least_one_uid_outside_the_int_range()
        {
            const int draws = 64;
            var uids = new long[draws];
            for (var i = 0; i < draws; i++)
            {
                uids[i] = new AddressUid(true).Uid;
            }

            Assert.All(uids, uid => Assert.NotEqual(0, uid));
            Assert.Contains(uids, uid => uid < 0 || uid > int.MaxValue);
        }
    }
}

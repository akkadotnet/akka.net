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
        /// Default uid generation (task 1.2) must stay in the legacy int range so a rolling upgrade from
        /// v1.5 is safe (Decision 2 of the widen-system-uid-to-64bit design).
        /// </summary>
        [Fact]
        public void AddressUidExtension_should_generate_a_uid_in_the_int_range_by_default()
        {
            var uid = AddressUidExtension.Uid(Sys);
            Assert.True(uid >= 0 && uid <= int.MaxValue);
        }

        /// <summary>
        /// With <c>akka.remote.use-64bit-system-uids = on</c>, generation is opt-in full-range 64-bit
        /// (task 1.3). A single draw could theoretically still land in the legacy int range, so this only
        /// asserts the uid is nonzero (zero is reserved as a sentinel) - see
        /// <see cref="AddressUid_64bit_generation_should_draw_at_least_one_uid_outside_the_int_range"/> for the
        /// statistical full-range check.
        /// </summary>
        [Fact]
        public void AddressUidExtension_should_generate_a_nonzero_uid_when_64bit_uids_are_enabled()
        {
            var sys2 = ActorSystem.Create("AddressUidExtensionSpecs64Bit",
                ConfigurationFactory.ParseString("akka.remote.use-64bit-system-uids = on"));
            try
            {
                var uid = AddressUidExtension.Uid(sys2);
                Assert.NotEqual(0, uid);
            }
            finally
            {
                Shutdown(sys2);
            }
        }

        /// <summary>
        /// Statistical check that <see cref="AddressUid"/>'s full-range 64-bit generator (task 1.2) actually
        /// draws outside the legacy <c>[0, int.MaxValue]</c> range: each draw lands in that legacy range with
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

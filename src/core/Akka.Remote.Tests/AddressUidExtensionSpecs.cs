//-----------------------------------------------------------------------
// <copyright file="AddressUidExtensionSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
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
            var values = new ConcurrentBag<int>();
            var parallelOps = 1000;
            var loop = Parallel.For(0, parallelOps, i =>
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
                sys2.Terminate().Wait();
            }
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="ActorUidSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    /// <summary>
    /// Actor UIDs come from a per-system counter when Artery is the configured transport,
    /// and from the legacy random 31-bit generator otherwise.
    /// </summary>
    public class ActorUidSpec : AkkaSpec
    {
        private const int ChildCount = 10_000;

        // Core only reads the flag; no remote provider is needed to exercise the UID path.
        private static readonly Config ArteryOn = ConfigurationFactory.ParseString("akka.remote.artery.enabled = on");

        private sealed class Noop : ReceiveActor
        {
            public Noop() => ReceiveAny(_ => { });
        }

        private sealed class Parent : ReceiveActor
        {
            public Parent()
            {
                Receive<int>(n =>
                {
                    var uids = new List<long>(n);
                    for (var i = 0; i < n; i++)
                        uids.Add(Context.ActorOf(Props.Create<Noop>()).Path.Uid);
                    Sender.Tell(uids);
                });
            }
        }

        private static long FirstUid(ActorSystem system) => system.ActorOf(Props.Create<Noop>()).Path.Uid;

        [Fact]
        public void Artery_actor_uids_should_be_distinct_positive_and_counter_sequential()
        {
            var system = ActorSystem.Create("artery-uids", ArteryOn);
            try
            {
                var parent = system.ActorOf(Props.Create<Parent>());
                var uids = parent.Ask<List<long>>(ChildCount, TimeSpan.FromSeconds(30)).Result;

                Assert.Equal(ChildCount, uids.Distinct().Count());
                Assert.All(uids, uid => Assert.True(uid > 0));
                // creation order from a single parent is strictly increasing
                for (var i = 1; i < uids.Count; i++)
                    Assert.True(uids[i] > uids[i - 1]);
            }
            finally
            {
                Shutdown(system);
            }
        }

        [Fact]
        public void Artery_actor_uids_should_not_repeat_across_consecutively_started_systems()
        {
            var first = ActorSystem.Create("artery-restart", ArteryOn);
            var firstUid = FirstUid(first);
            Shutdown(first);
            var second = ActorSystem.Create("artery-restart", ArteryOn);
            try
            {
                Assert.NotEqual(firstUid, FirstUid(second));
            }
            finally
            {
                Shutdown(second);
            }
        }

        [Fact]
        public void Actor_uids_should_stay_within_int_range_without_artery()
        {
            var parent = Sys.ActorOf(Props.Create<Parent>());
            var uids = parent.Ask<List<long>>(1_000, TimeSpan.FromSeconds(30)).Result;

            Assert.All(uids, uid => Assert.InRange(uid, 1L, int.MaxValue));
        }
    }
}

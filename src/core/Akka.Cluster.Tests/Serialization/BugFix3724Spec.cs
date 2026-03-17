//-----------------------------------------------------------------------
// <copyright file="BugFix3724Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests.Serialization
{
    /// <summary>
    ///     https://github.com/akkadotnet/akka.net/issues/3724
    ///     Used to validate that `akka.actor.serialize-messages = on` works while
    ///     using Akka.Cluster
    /// </summary>
    public class BugFix3724Spec : AkkaSpec
    {
        public BugFix3724Spec(ITestOutputHelper helper)
            : base("""
                   akka.actor.provider = cluster
                    akka.actor.serialize-messages = on
                   """, helper)
        {
            _cluster = Cluster.Get(Sys);
            _selfAddress = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
        }

        private readonly Address _selfAddress;
        private readonly Cluster _cluster;

        [Fact(DisplayName = "Should be able to use 'akka.actor.serialize-messages' while running Akka.Cluster")]
        public async Task Should_serialize_all_AkkaCluster_messages()
        {
            _cluster.Subscribe(TestActor, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents,
                typeof(ClusterEvent.MemberUp));
            await WithinAsync(TimeSpan.FromSeconds(10), async () =>
            {
                // Expect 0 means we have to wait for the full duration
                await EventFilter.Exception<Exception>().ExpectAsync(0, async () =>
                {
                    // wait for a singleton cluster to fully form and publish a member up event
                    await _cluster.JoinAsync(_selfAddress);
                    var up = await ExpectMsgAsync<ClusterEvent.MemberUp>();
                    up.Member.Address.Should().Be(_selfAddress);
                });
            });
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="ProxyShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class InactiveEntityPassivationSpec : AkkaSpec
    {
        #region Protocol

        internal class Passivate
        {
            public static readonly Passivate Instance = new Passivate();
            private Passivate() { }
        }

        internal class Entity : UntypedActor
        {
            private readonly string _id = Context.Self.Path.Name;

            public IActorRef Probe { get; }

            public static Props Props(IActorRef probe) =>
                Actor.Props.Create(() => new Entity(probe));

            public Entity(IActorRef probe)
            {
                Probe = probe;
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case Passivate _:
                        Probe.Tell($"{_id} passivating");
                        Context.Stop(Self);
                        break;
                    default:
                        Probe.Tell(new GotIt(_id, message, DateTime.Now.Ticks));
                        break;
                }
            }

            public class GotIt
            {
                public string Id { get; }
                public object Msg { get; }
                public long When { get; }

                public GotIt(string id, object msg, long when)
                {
                    Id = id;
                    Msg = msg;
                    When = when;
                }
            }
        }

        #endregion

        private readonly ExtractEntityId _extractEntityId = message =>
            message is int msg ? Tuple.Create(msg.ToString(), message) : null;

        private readonly ExtractShardId _extractShard = message =>
            message is int msg ? (msg % 10).ToString(CultureInfo.InvariantCulture) : null;

        public InactiveEntityPassivationSpec()
            : base(GetConfig())
        { }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.cluster.sharding.passivate-idle-entity-after = 3s")
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig());
        }

        [Fact]
        public void Passivation_of_inactive_entities_must_passivate_entities_when_they_have_not_seen_messages_for_the_configured_duration()
        {
            // Single node cluster
            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);

            var probe = new TestProbe(Sys, new XunitAssertions());
            var settings = ClusterShardingSettings.Create(Sys);
            var region = ClusterSharding.Get(Sys).Start(
                "myType",
                Entity.Props(probe.Ref),
                settings,
                _extractEntityId,
                _extractShard,
                new LeastShardAllocationStrategy(10, 3),
                Passivate.Instance);
            
            region.Tell(1);
            region.Tell(2);

            var responses = new[]
            {
                probe.ExpectMsg<Entity.GotIt>(),
                probe.ExpectMsg<Entity.GotIt>()
            };
            responses.Select(r => r.Id).Should().BeEquivalentTo("1", "2");
            var timeOneSawMessage = responses.Single(r => r.Id == "1").When;

            Thread.Sleep(1000);
            region.Tell(2);
            probe.ExpectMsg<Entity.GotIt>().Id.ShouldBe("2");
            Thread.Sleep(1000);
            region.Tell(2);
            probe.ExpectMsg<Entity.GotIt>().Id.ShouldBe("2");

            // Make sure "1" hasn't seen a message in 3 seconds and passivates
            var timeSinceOneSawAMessage = DateTime.Now.Ticks - timeOneSawMessage;
            probe.ExpectNoMsg(TimeSpan.FromSeconds(3) - TimeSpan.FromTicks(timeSinceOneSawAMessage));
            probe.ExpectMsg("1 passivating");

            // But it can be re-activated just fine
            region.Tell(1);
            region.Tell(2);

            responses = new[]
            {
                probe.ExpectMsg<Entity.GotIt>(),
                probe.ExpectMsg<Entity.GotIt>()
            };
            responses.Select(r => r.Id).Should().BeEquivalentTo("1", "2");
        }
    }
}

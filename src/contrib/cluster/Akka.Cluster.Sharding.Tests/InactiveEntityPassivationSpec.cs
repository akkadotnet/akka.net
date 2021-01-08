//-----------------------------------------------------------------------
// <copyright file="InactiveEntityPassivationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class AbstractInactiveEntityPassivationSpec : AkkaSpec
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

                public override int GetHashCode()
                {
                    return Id.GetHashCode();
                }

                public override bool Equals(object obj)
                {
                    if (obj is GotIt other)
                        return Id == other.Id;
                    return false;
                }
            }
        }

        #endregion

        protected ClusterShardingSettings settings;
        protected readonly TimeSpan smallTolerance = TimeSpan.FromMilliseconds(300);

        private readonly ExtractEntityId _extractEntityId = message =>
            message is int msg ? (msg.ToString(), message) : Option<(string, object)>.None;

        private readonly ExtractShardId _extractShard = message =>
            message is int msg ? (msg % 10).ToString(CultureInfo.InvariantCulture) : null;

        public AbstractInactiveEntityPassivationSpec(Config config, ITestOutputHelper helper)
            : base(config.WithFallback(GetConfig()), helper)
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.cluster.sharding.passivate-idle-entity-after = 3s
                akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
                akka.remote.dot-netty.tcp.port = 0")
                .WithFallback(ClusterSharding.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig());
        }

        protected IActorRef Start(TestProbe probe)
        {
            settings = ClusterShardingSettings.Create(Sys);
            // single node cluster
            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);

            return ClusterSharding.Get(Sys).Start(
                "myType",
                Entity.Props(probe.Ref),
                settings,
                _extractEntityId,
                _extractShard,
                ClusterSharding.Get(Sys).DefaultShardAllocationStrategy(settings),
                Passivate.Instance);
        }

        protected async Task<TimeSpan> TimeUntilPassivate(IActorRef region, TestProbe probe)
        {
            Entity.GotIt[] responses = null;
            await AwaitAssertAsync(() =>
            {
                region.Tell(1);
                region.Tell(2);
                
                responses = new[]
                {
                    probe.ExpectMsg<Entity.GotIt>(),
                    probe.ExpectMsg<Entity.GotIt>()
                };
                responses.Select(r => r.Id).Should().BeEquivalentTo("1", "2");
            }, TimeSpan.FromSeconds(20));
            
            var timeOneSawMessage = responses.Single(r => r.Id == "1").When;

            await Task.Delay(1000);
            region.Tell(2);
            probe.ExpectMsg<Entity.GotIt>(TimeSpan.FromSeconds(10)).Id.ShouldBe("2");
            await Task.Delay(1000);
            region.Tell(2);
            probe.ExpectMsg<Entity.GotIt>(TimeSpan.FromSeconds(10)).Id.ShouldBe("2");

            var timeSinceOneSawAMessage = DateTime.Now.Ticks - timeOneSawMessage;
            return settings.PassivateIdleEntityAfter - TimeSpan.FromTicks(timeSinceOneSawAMessage) + smallTolerance;
        }
    }

    public class InactiveEntityPassivationSpec : AbstractInactiveEntityPassivationSpec
    {
        public InactiveEntityPassivationSpec(ITestOutputHelper helper)
            : base(ConfigurationFactory.ParseString(@"akka.cluster.sharding.passivate-idle-entity-after = 3s"), helper)
        {
        }

        [Fact]
        public async Task Passivation_of_inactive_entities_must_passivate_entities_when_they_have_not_seen_messages_for_the_configured_duration()
        {
            var probe = CreateTestProbe();
            var region = Start(probe);

            // make sure "1" hasn't seen a message in 3 seconds and passivates
            var time = await TimeUntilPassivate(region, probe);
            probe.ExpectNoMsg(time);

            await AwaitAssertAsync(() =>
            {
                // but it can be re activated
                region.Tell(1);
                region.Tell(2);
                
                var responses = new[]
                {
                    probe.ExpectMsg<Entity.GotIt>(),
                    probe.ExpectMsg<Entity.GotIt>()
                };
                responses.Select(r => r.Id).Should().BeEquivalentTo("1", "2");
            }, TimeSpan.FromSeconds(20));
        }
    }

    public class DisabledInactiveEntityPassivationSpec : AbstractInactiveEntityPassivationSpec
    {
        public DisabledInactiveEntityPassivationSpec(ITestOutputHelper helper)
            : base(ConfigurationFactory.ParseString(@"akka.cluster.sharding.passivate-idle-entity-after = off"), helper)
        {
        }

        [Fact]
        public async Task Passivation_of_inactive_entities_must_not_passivate_when_passivation_is_disabled()
        {
            var probe = CreateTestProbe();
            var region = Start(probe);
            var time = await TimeUntilPassivate(region, probe);
            probe.ExpectNoMsg(time);
        }
    }
}

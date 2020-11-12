//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesBatchedUpdatesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class RememberEntitiesBatchedUpdatesSpec : AkkaSpec
    {
        private class EntityEnvelope
        {
            public EntityEnvelope(int id, object msg)
            {
                Id = id;
                Msg = msg;
            }

            public int Id { get; }
            public object Msg { get; }
        }

        private class EntityActor : ActorBase
        {
            private readonly IActorRef probe;

            public class Started
            {
                public Started(int id)
                {
                    Id = id;
                }

                public int Id { get; }

                #region Equals

                /// <inheritdoc/>
                public override bool Equals(object obj)
                {
                    return Equals(obj as Started);
                }

                public bool Equals(Started other)
                {
                    if (ReferenceEquals(other, null)) return false;
                    if (ReferenceEquals(other, this)) return true;

                    return Id.Equals(other.Id);
                }

                /// <inheritdoc/>
                public override int GetHashCode()
                {
                    return Id.GetHashCode();
                }

                #endregion
            }

            public class Stopped
            {
                public Stopped(int id)
                {
                    Id = id;
                }

                public int Id { get; }
            }

            public static Props Props(IActorRef probe)
            {
                return Actor.Props.Create(() => new EntityActor(probe));

            }
            private ILoggingAdapter log = Context.GetLogger();

            public EntityActor(IActorRef probe)
            {
                probe.Tell(new Started(int.Parse(Self.Path.Name)));
                this.probe = probe;
            }


            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case "stop":
                        log.Debug("Got stop message, stopping");
                        Context.Stop(Self);
                        return true;
                    case "graceful-stop":
                        log.Debug("Got a graceful stop, requesting passivation");
                        Context.Parent.Tell(new Passivate("stop"));
                        return true;
                    case "start":
                        log.Debug("Got a start");
                        return true;
                    case "ping":
                        return true;
                }
                return false;
            }

            protected override void PostStop()
            {
                probe.Tell(new Stopped(int.Parse(Self.Path.Name)));
            }
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"
                akka.loglevel=DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.state-store-mode = ddata
                akka.cluster.sharding.remember-entities = on

                # no leaks between test runs thank you
                akka.cluster.sharding.distributed-data.durable.keys = []
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")

                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(DistributedData.DistributedData.DefaultConfig())
                .WithFallback(ClusterSingletonManager.DefaultConfig());
        }

        public RememberEntitiesBatchedUpdatesSpec(ITestOutputHelper helper) : base(GetConfig(), helper)
        {
        }

        protected override void AtStartup()
        {
            // Form a one node cluster
            var cluster = Cluster.Get(Sys);
            cluster.Join(cluster.SelfAddress);
            AwaitAssert(() =>
            {
                cluster.ReadView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1);
            });
        }

        [Fact]
        public void Batching_of_starts_and_stops_must_work()
        {
            var probe = CreateTestProbe();
            var sharding = ClusterSharding.Get(Sys).Start(
                "batching",
                EntityActor.Props(probe.Ref),
                ClusterShardingSettings.Create(Sys),
                ExtractEntityId,
                ExtractShardId);

            // make sure that sharding is up and running
            sharding.Tell(new EntityEnvelope(0, "ping"), probe.Ref);
            probe.ExpectMsg(new EntityActor.Started(0));

            // start 20, should write first and batch the rest
            for (int i = 1; i <= 20; i++)
            {
                sharding.Tell(new EntityEnvelope(i, "start"));
            }
            probe.ReceiveN(20);

            // start 20 more, and stop the previous ones that are already running,
            // should create a mixed batch of start + stops
            for (int i = 21; i <= 40; i++)
            {
                sharding.Tell(new EntityEnvelope(i, "start"));
                sharding.Tell(new EntityEnvelope(i - 20, "graceful-stop"));
            }
            probe.ReceiveN(40);
            // stop the last 20, should batch stops only
            for (int i = 21; i <= 40; i++)
            {
                sharding.Tell(new EntityEnvelope(i, "graceful-stop"));
            }
            probe.ReceiveN(20);
        }

        private Option<(string, object)> ExtractEntityId(object message)
        {
            switch (message)
            {
                case EntityEnvelope e:
                    return (e.Id.ToString(), e.Msg);
            }
            throw new NotSupportedException();
        }

        private string ExtractShardId(object message)
        {
            switch (message)
            {
                case EntityEnvelope _:
                    return "1"; // single shard for all entities
                case ShardRegion.StartEntity _:
                    return "1";
            }
            throw new NotSupportedException();
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="RememberEntitiesFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
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
    public class RememberEntitiesFailureSpec : AkkaSpec
    {
        internal class EntityActor : ActorBase
        {
            private readonly ILoggingAdapter log = Context.GetLogger();

            public EntityActor()
            {
                log.Info("Entity actor [{0}] starting up", Context.Self.Path.Name);
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case "stop":
                        log.Info("Stopping myself!");
                        Context.Stop(Self);
                        return true;
                    case "graceful-stop":
                        Context.Parent.Tell(new Passivate("stop"));
                        return true;
                    case var msg:
                        Sender.Tell(msg);
                        return true;
                }
            }
        }

        private class EntityEnvelope
        {
            public EntityEnvelope(int entityId, object msg)
            {
                EntityId = entityId;
                Msg = msg;
            }

            public int EntityId { get; }
            public object Msg { get; }
        }

        private readonly ExtractEntityId extractEntityId = message =>
        {
            if (message is EntityEnvelope e)
                return (e.EntityId.ToString(), e.Msg);
            return Option<(string, object)>.None;
        };

        private readonly ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case EntityEnvelope e:
                    return (e.EntityId % 10).ToString();
            }
            return null;
        };

        private interface IFail
        {
        }

        private class NoResponse : IFail
        {
            public override string ToString() => "NoResponse";
        }

        private class CrashStore : IFail
        {
            public override string ToString() => "CrashStore";
        }

        private class StopStore : IFail
        {
            public override string ToString() => "StopStore";
        }

        private class Delay : IFail
        {
            public Delay(TimeSpan howLong)
            {
                HowLong = howLong;
            }

            public TimeSpan HowLong { get; }

            public override string ToString() => $"Delay{HowLong.TotalMilliseconds}";
        }

        // outside store since we need to be able to set them before sharding initializes
        private static ImmutableDictionary<string, IFail> failShardGetEntities = ImmutableDictionary<string, IFail>.Empty;
        private static readonly IFail failCoordinatorGetShards = null;

        private class ShardStoreCreated
        {
            public ShardStoreCreated(IActorRef store, string shardId)
            {
                Store = store;
                ShardId = shardId;
            }

            public IActorRef Store { get; }
            public string ShardId { get; }
        }

        private class CoordinatorStoreCreated
        {
            public CoordinatorStoreCreated(IActorRef store)
            {
                Store = store;
            }

            public IActorRef Store { get; }
        }

        internal class FakeStore : IRememberEntitiesProvider
        {
            public FakeStore(ClusterShardingSettings settings, string typeName)
            {
            }

            public Props ShardStoreProps(string shardId)
            {
                return FakeShardStoreActor.Props(shardId);
            }

            public Props CoordinatorStoreProps()
            {
                return FakeCoordinatorStoreActor.Props();
            }
        }

        private class FakeShardStoreActor : ActorBase, IWithTimers
        {
            public static Props Props(string shardId) => Actor.Props.Create(() => new FakeShardStoreActor(shardId));

            internal class FailUpdateEntity
            {
                public FailUpdateEntity(IFail whichWay)
                {
                    WhichWay = whichWay;
                }

                public IFail WhichWay { get; }
            }

            internal class ClearFail
            {
            }

            internal class Delayed
            {
                public Delayed(IActorRef replyTo, object msg)
                {
                    ReplyTo = replyTo;
                    Msg = msg;
                }

                public IActorRef ReplyTo { get; }
                public object Msg { get; }
            }

            private readonly string shardId;
            private IFail failUpdate;
            private readonly ILoggingAdapter log = Context.GetLogger();

            public FakeShardStoreActor(string shardId)
            {
                this.shardId = shardId;
                Context.System.EventStream.Publish(new ShardStoreCreated(Self, shardId));
            }

            public ITimerScheduler Timers { get; set; }

            //protected override void PreStart()
            //{
            //    Context.System.EventStream.Publish(new ShardStoreCreated(Self, shardId));
            //    base.PreStart();
            //}

            //protected override void PostRestart(Exception reason)
            //{
            //}

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case RememberEntitiesShardStore.GetEntities _:
                        switch (failShardGetEntities.GetValueOrDefault(shardId))
                        {
                            case null:
                                Sender.Tell(new RememberEntitiesShardStore.RememberedEntities(ImmutableHashSet<string>.Empty));
                                return true;
                            case NoResponse _:
                                log.Debug("Sending no response for GetEntities");
                                return true;
                            case CrashStore _:
                                throw new ApplicationException("store crash on GetEntities");
                            case StopStore _:
                                Context.Stop(Self);
                                return true;
                            case Delay f:
                                log.Debug("Delaying initial entities listing with {0}", f.HowLong);
                                Timers.StartSingleTimer("get-entities-delay", new Delayed(Sender, ImmutableHashSet<string>.Empty), f.HowLong);
                                return true;
                        }
                        return true;
                    case RememberEntitiesShardStore.Update m:
                        switch (failUpdate)
                        {
                            case null:
                                Sender.Tell(new RememberEntitiesShardStore.UpdateDone(m.Started, m.Stopped));
                                return true;
                            case NoResponse _:
                                log.Debug("Sending no response for AddEntity");
                                return true;
                            case CrashStore _:
                                throw new ApplicationException("store crash on AddEntity");
                            case StopStore _:
                                Context.Stop(Self);
                                return true;
                            case Delay f:
                                log.Debug("Delaying response for AddEntity with {0}", f.HowLong);
                                Timers.StartSingleTimer("add-entity-delay", new Delayed(Sender, ImmutableHashSet<string>.Empty), f.HowLong);
                                return true;
                        }
                        return true;
                    case FailUpdateEntity m:
                        failUpdate = m.WhichWay;
                        Sender.Tell(Done.Instance);
                        return true;
                    case ClearFail _:
                        failUpdate = null;
                        Sender.Tell(Done.Instance);
                        return true;
                    case Delayed m:
                        m.ReplyTo.Tell(m.Msg);
                        return true;
                }
                return false;
            }
        }

        private class FakeCoordinatorStoreActor : ActorBase, IWithTimers
        {
            public static Props Props() => Actor.Props.Create(() => new FakeCoordinatorStoreActor());

            internal class FailAddShard
            {
                public FailAddShard(string shardId, IFail wayToFail)
                {
                    ShardId = shardId;
                    WayToFail = wayToFail;
                }

                public string ShardId { get; }
                public IFail WayToFail { get; }
            }

            internal class ClearFailShard
            {
                public ClearFailShard(string shardId)
                {
                    ShardId = shardId;
                }

                public string ShardId { get; }
            }

            private readonly ILoggingAdapter log = Context.GetLogger();
            private ImmutableDictionary<string, IFail> failAddShard = ImmutableDictionary<string, IFail>.Empty;

            public ITimerScheduler Timers { get; set; }

            public FakeCoordinatorStoreActor()
            {
                Context.System.EventStream.Publish(new CoordinatorStoreCreated(Context.Self));
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case RememberEntitiesCoordinatorStore.GetShards _:
                        switch (failCoordinatorGetShards)
                        {
                            case null:
                                Sender.Tell(new RememberEntitiesCoordinatorStore.RememberedShards(ImmutableHashSet<string>.Empty));
                                return true;
                            case NoResponse _:
                                return true;
                            case CrashStore _:
                                throw new ApplicationException("store crash on load");
                            case StopStore _:
                                Context.Stop(Self);
                                return true;
                            case Delay f:
                                log.Debug("Delaying initial shard listing with {0}", f.HowLong);
                                Timers.StartSingleTimer("list-shards-delay", new FakeShardStoreActor.Delayed(Sender, ImmutableHashSet<string>.Empty), f.HowLong);
                                return true;
                        }
                        return true;
                    case RememberEntitiesCoordinatorStore.AddShard m:
                        switch (failAddShard.GetValueOrDefault(m.ShardId))
                        {
                            case null:
                                Sender.Tell(new RememberEntitiesCoordinatorStore.UpdateDone(m.ShardId));
                                return true;
                            case NoResponse _:
                                return true;
                            case CrashStore _:
                                throw new ApplicationException("store crash on add");
                            case StopStore _:
                                Context.Stop(Self);
                                return true;
                            case Delay f:
                                log.Debug("Delaying adding shard with {0}", f.HowLong);
                                Timers.StartSingleTimer("add-shard-delay", new FakeShardStoreActor.Delayed(Sender, ImmutableHashSet<string>.Empty), f.HowLong);
                                return true;
                        }
                        return true;
                    case FailAddShard m:
                        log.Debug("Failing store of {0} with {1}", m.ShardId, m.WayToFail);
                        failAddShard = failAddShard.SetItem(m.ShardId, m.WayToFail);
                        Sender.Tell(Done.Instance);
                        return true;
                    case ClearFailShard m:
                        log.Debug("No longer failing store of {0}", m.ShardId);
                        failAddShard = failAddShard.Remove(m.ShardId);
                        Sender.Tell(Done.Instance);
                        return true;
                    case FakeShardStoreActor.Delayed m:
                        m.ReplyTo.Tell(m.Msg);
                        return true;
                }
                return false;
            }
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.distributed-data.durable.keys = []
                # must be ddata or else remember entities store is ignored
                akka.cluster.sharding.state-store-mode = ddata
                akka.cluster.sharding.remember-entities = on
                akka.cluster.sharding.remember-entities-store = custom
                akka.cluster.sharding.remember-entities-custom-store = ""Akka.Cluster.Sharding.Tests.RememberEntitiesFailureSpec+FakeStore, Akka.Cluster.Sharding.Tests""
                # quick backoffs
                akka.cluster.sharding.entity-restart-backoff = 1s
                akka.cluster.sharding.shard-failure-backoff = 1s
                akka.cluster.sharding.coordinator-failure-backoff = 1s
                akka.cluster.sharding.updating-state-timeout = 1s
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")
                .WithFallback(ClusterSingletonManager.DefaultConfig()
                .WithFallback(ClusterSharding.DefaultConfig()));

        public RememberEntitiesFailureSpec(ITestOutputHelper helper) : base(SpecConfig, helper)
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
        public void Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails_NoResponse()
        {
            Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails(new NoResponse());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails_CrashStore()
        {
            Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails(new CrashStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails_StopStore()
        {
            Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails(new StopStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails_Delay_500()
        {
            Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails(new Delay(TimeSpan.FromMilliseconds(500)));
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails_Delay_1000()
        {
            Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails(new Delay(TimeSpan.FromSeconds(1)));
        }

        private void Remember_entities_handling_in_sharding_must_recover_when_initial_remember_entities_load_fails(IFail wayToFail)
        {
            Log.Debug("Getting entities for shard 1 will fail");
            failShardGetEntities = ImmutableDictionary<string, IFail>.Empty.Add("1", wayToFail);

            try
            {
                var probe = CreateTestProbe();
                var sharding = ClusterSharding.Get(Sys).Start(
                    $"initial-{wayToFail}",
                    Props.Create(() => new EntityActor()),
                    ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                    extractEntityId,
                    extractShardId);

                sharding.Tell(new EntityEnvelope(1, "hello-1"), probe.Ref);
                probe.ExpectNoMsg(); // message is lost because shard crashes

                Log.Debug("Resetting initial fail");
                failShardGetEntities = ImmutableDictionary<string, IFail>.Empty;

                // shard should be restarted and eventually succeed
                AwaitAssert(() =>
                {
                    sharding.Tell(new EntityEnvelope(1, "hello-1"), probe.Ref);
                    probe.ExpectMsg("hello-1");
                });

                Sys.Stop(sharding);
            }
            finally
            {
                failShardGetEntities = ImmutableDictionary<string, IFail>.Empty;
            }
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails_NoResponse()
        {
            Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails(new NoResponse());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails_CrashStore()
        {
            Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails(new CrashStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails_StopStore()
        {
            Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails(new StopStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails_Delay_500()
        {
            Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails(new Delay(TimeSpan.FromMilliseconds(500)));
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails_Delay_1000()
        {
            Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails(new Delay(TimeSpan.FromSeconds(1)));
        }

        private void Remember_entities_handling_in_sharding_must_recover_when_shard_storing_a_start_event_fails(IFail wayToFail)
        {
            var storeProbe = CreateTestProbe();
            Sys.EventStream.Subscribe(storeProbe.Ref, typeof(ShardStoreCreated));

            var sharding = ClusterSharding.Get(Sys).Start(
                $"shardStoreStart-{wayToFail}",
                Props.Create(() => new EntityActor()),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                extractEntityId,
                extractShardId);

            // trigger shard start and store creation
            var probe = CreateTestProbe();
            sharding.Tell(new EntityEnvelope(1, "hello-1"), probe.Ref);
            var shardStore = storeProbe.ExpectMsg<ShardStoreCreated>().Store;
            probe.ExpectMsg("hello-1");

            // hit shard with other entity that will fail
            shardStore.Tell(new FakeShardStoreActor.FailUpdateEntity(wayToFail), storeProbe.Ref);
            storeProbe.ExpectMsg<Done>();

            sharding.Tell(new EntityEnvelope(11, "hello-11"), probe.Ref);

            // do we get an answer here? shard crashes
            probe.ExpectNoMsg();
            if (wayToFail is StopStore || wayToFail is CrashStore)
            {
                // a new store should be started
                shardStore = storeProbe.ExpectMsg<ShardStoreCreated>().Store;
            }

            if (wayToFail is NoResponse || wayToFail is Delay || wayToFail is CrashStore)
            {
                // a new store should be started
                shardStore = storeProbe.ExpectMsg<ShardStoreCreated>().Store;
            }

            var stopFailingProbe = CreateTestProbe();
            shardStore.Tell(new FakeShardStoreActor.ClearFail(), stopFailingProbe.Ref);
            stopFailingProbe.ExpectMsg<Done>();

            // it takes a while - timeout hits and then backoff
            AwaitAssert(() =>
            {
                sharding.Tell(new EntityEnvelope(11, "hello-11-2"), probe.Ref);
                probe.ExpectMsg("hello-11-2");
            }, TimeSpan.FromSeconds(10));
            Sys.Stop(sharding);
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails_NoResponse()
        {
            Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails(new NoResponse());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails_CrashStore()
        {
            Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails(new CrashStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails_StopStore()
        {
            Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails(new StopStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails_Delay_500()
        {
            Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails(new Delay(TimeSpan.FromMilliseconds(500)));
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails_Delay_1000()
        {
            Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails(new Delay(TimeSpan.FromSeconds(1)));
        }

        private void Remember_entities_handling_in_sharding_must_recover_on_abrupt_entity_stop_when_storing_a_stop_event_fails(IFail wayToFail)
        {
            var storeProbe = CreateTestProbe();
            Sys.EventStream.Subscribe(storeProbe.Ref, typeof(ShardStoreCreated));

            var sharding = ClusterSharding.Get(Sys).Start(
                $"shardStoreStopAbrupt-{wayToFail}",
                Props.Create(() => new EntityActor()),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                extractEntityId,
                extractShardId);

            var probe = CreateTestProbe();

            // trigger shard start and store creation
            sharding.Tell(new EntityEnvelope(1, "hello-1"), probe.Ref);
            var shard1Store = storeProbe.ExpectMsg<ShardStoreCreated>().Store;
            probe.ExpectMsg("hello-1");

            // fail it when stopping
            shard1Store.Tell(new FakeShardStoreActor.FailUpdateEntity(wayToFail), storeProbe.Ref);
            storeProbe.ExpectMsg<Done>();

            // FIXME restart without passivating is not saved and re-started again without storing the stop so this isn't testing anything
            sharding.Tell(new EntityEnvelope(1, "stop"));

            shard1Store.Tell(new FakeShardStoreActor.ClearFail(), storeProbe.Ref);
            storeProbe.ExpectMsg<Done>();

            // it takes a while - timeout hits and then backoff
            AwaitAssert(() =>
            {
                sharding.Tell(new EntityEnvelope(1, "hello-2"), probe.Ref);
                probe.ExpectMsg("hello-2");
            }, TimeSpan.FromSeconds(10));
            Sys.Stop(sharding);
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails_NoResponse()
        {
            Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails(new NoResponse());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails_CrashStore()
        {
            Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails(new CrashStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails_StopStore()
        {
            Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails(new StopStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails_Delay_500()
        {
            Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails(new Delay(TimeSpan.FromMilliseconds(500)));
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails_Delay_1000()
        {
            Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails(new Delay(TimeSpan.FromSeconds(1)));
        }

        private void Remember_entities_handling_in_sharding_must_recover_on_graceful_entity_stop_when_storing_a_stop_event_fails(IFail wayToFail)
        {
            var storeProbe = CreateTestProbe();
            Sys.EventStream.Subscribe(storeProbe.Ref, typeof(ShardStoreCreated));

            var sharding = ClusterSharding.Get(Sys).Start(
                $"shardStoreStopGraceful-{wayToFail}",
                Props.Create(() => new EntityActor()),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                extractEntityId,
                extractShardId,
                ShardAllocationStrategy.LeastShardAllocationStrategy(absoluteLimit: 1, relativeLimit: 0.1),
                "graceful-stop");

            var probe = CreateTestProbe();

            // trigger shard start and store creation
            sharding.Tell(new EntityEnvelope(1, "hello-1"), probe.Ref);
            var shard1Store = storeProbe.ExpectMsg<ShardStoreCreated>().Store;
            probe.ExpectMsg("hello-1");

            // fail it when stopping
            shard1Store.Tell(new FakeShardStoreActor.FailUpdateEntity(wayToFail), storeProbe.Ref);
            storeProbe.ExpectMsg<Done>();

            sharding.Tell(new EntityEnvelope(1, "graceful-stop"));

            if (!(wayToFail is CrashStore) && !(wayToFail is StopStore))
            {
                // race, give the shard some time to see the passivation before restoring the fake shard store
                Thread.Sleep(250);
                shard1Store.Tell(new FakeShardStoreActor.ClearFail(), probe.Ref);
                probe.ExpectMsg<Done>();
            }

            // it takes a while?
            AwaitAssert(() =>
            {
                sharding.Tell(new EntityEnvelope(1, "hello-2"), probe.Ref);
                probe.ExpectMsg("hello-2");
            }, TimeSpan.FromSeconds(5));
            Sys.Stop(sharding);
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails_NoResponse()
        {
            Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails(new NoResponse());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails_CrashStore()
        {
            Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails(new CrashStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails_StopStore()
        {
            Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails(new StopStore());
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails_Delay_500()
        {
            Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails(new Delay(TimeSpan.FromMilliseconds(500)));
        }

        [Fact]
        public void Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails_Delay_1000()
        {
            Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails(new Delay(TimeSpan.FromSeconds(1)));
        }

        private void Remember_entities_handling_in_sharding_must_recover_when_coordinator_storing_shard_start_fails(IFail wayToFail)
        {
            var storeProbe = CreateTestProbe();
            Sys.EventStream.Subscribe(storeProbe.Ref, typeof(CoordinatorStoreCreated));

            var sharding = ClusterSharding.Get(Sys).Start(
                $"coordinatorStoreStopGraceful-{wayToFail}",
                Props.Create(() => new EntityActor()),
                ClusterShardingSettings.Create(Sys).WithRememberEntities(true),
                extractEntityId,
                extractShardId,
                ShardAllocationStrategy.LeastShardAllocationStrategy(absoluteLimit: 1, relativeLimit: 0.1),
                "graceful-stop");

            var probe = CreateTestProbe();

            // coordinator store is triggered by coordinator starting up
            var coordinatorStore = storeProbe.ExpectMsg<CoordinatorStoreCreated>().Store;
            coordinatorStore.Tell(new FakeCoordinatorStoreActor.FailAddShard("1", wayToFail), probe.Ref);
            probe.ExpectMsg<Done>();

            sharding.Tell(new EntityEnvelope(1, "hello-1"), probe.Ref);
            probe.ExpectNoMsg(TimeSpan.FromSeconds(1)); // because shard cannot start while store failing

            if (wayToFail is StopStore || wayToFail is CrashStore)
            {
                // a new store should be started
                coordinatorStore = storeProbe.ExpectMsg<CoordinatorStoreCreated>().Store;
            }

            // fail it when stopping
            coordinatorStore.Tell(new FakeCoordinatorStoreActor.ClearFailShard("1"), storeProbe.Ref);
            storeProbe.ExpectMsg<Done>();

            probe.AwaitAssert(() =>
            {
                sharding.Tell(new EntityEnvelope(1, "hello-2"), probe.Ref);
                probe.ExpectMsg("hello-2"); // should now work again
            }, TimeSpan.FromSeconds(5));

            Sys.Stop(sharding);
        }
    }
}

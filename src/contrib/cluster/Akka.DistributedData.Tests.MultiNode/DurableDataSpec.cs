﻿#region copyright
// -----------------------------------------------------------------------
//  <copyright file="DurableDataSpec.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.DistributedData.Durable;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.DistributedData.Tests.MultiNode
{
    public class DurableDataSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public DurableDataSpecConfig(bool writeBehind)
        {
            First = Role("first");
            Second = Role("second");

            var writeBehindInterval = writeBehind ? "200ms" : "off";
            CommonConfig = ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            akka.log-dead-letters-during-shutdown = off
            akka.cluster.distributed-data.durable.keys = [""durable*""]
            akka.cluster.distributed-data.durable.lmdb {{
              dir = target/DurableDataSpec-${DateTime.UtcNow.Ticks}-ddata
              map-size = 10 MiB
              write-behind-interval = ${writeBehindInterval}
            }}
            akka.test.single-expect-default = 5s
            ").WithFallback(DistributedData.DefaultConfig());
        }
    }

    internal sealed class TestDurableStore : ReceiveActor
    {
        public TestDurableStore(bool failLoad, bool failStore)
        {
            Receive<LoadAll>(_ =>
            {
                if (failLoad) throw new LoadFailedException("Failed to load durable distributed-data");
                else Sender.Tell(LoadAllCompleted.Instance);
            });
            Receive<Store>(store =>
            {
                var reply = store.Reply;
                reply?.ReplyTo.Tell(failStore ? reply.FailureMessage : reply.SuccessMessage);
            });
        }
    }

    public abstract class DurableDataSpec : MultiNodeClusterSpec
    {
        public static Props TestDurableStoreProps(bool failLoad = false, bool failStore = false)
        {
            return Props.Create(() => new TestDurableStore(failLoad, failStore));
        }

        private readonly RoleName first;
        private readonly RoleName second;
        private readonly Cluster.Cluster cluster;
        private readonly TimeSpan timeout = TimeSpan.FromSeconds(5);
        private readonly IWriteConsistency writeTwo;
        private readonly IReadConsistency readTwo;

        private readonly GCounterKey keyA = new GCounterKey("durable-A");
        private readonly GCounterKey keyB = new GCounterKey("durable-B");
        private readonly ORSetKey<string> keyC = new ORSetKey<string>("durable-C");

        private int testStepCounter = 0;

        protected DurableDataSpec(DurableDataSpecConfig config) : base(config)
        {
            InitialParticipantsValueFactory = Roles.Count;
            cluster = Akka.Cluster.Cluster.Get(Sys);
            writeTwo = new WriteTo(2, timeout);
            readTwo = new ReadFrom(2, timeout);
            first = config.First;
            second = config.Second;
        }

        protected override int InitialParticipantsValueFactory { get; }

        [MultiNodeFact(Skip = "FIXME")]
        public void DurableDataSpec_Tests()
        {
            Durable_CRDT_should_work_in_a_single_node_cluster();
            Durable_CRDT_should_work_in_a_multi_node_cluster();
            Durable_CRDT_should_be_durable_after_gossip_update();
            Durable_CRDT_should_handle_Update_before_Load();
            Durable_CRDT_should_stop_Replicator_if_Load_fails();
            Durable_CRDT_should_reply_with_StoreFailure_if_store_fails();
        }

        public void Durable_CRDT_should_work_in_a_single_node_cluster()
        {
            Join(first, second);

            RunOn(() =>
            {
                var r = NewReplicator(Sys);
                Within(TimeSpan.FromSeconds(10), () =>
               {
                   AwaitAssert(() =>
                   {
                       r.Tell(Dsl.GetReplicaCount);
                       ExpectMsg(new ReplicaCount(1));
                   });
               });

                r.Tell(Dsl.Get(keyA, ReadLocal.Instance));
                ExpectMsg(new NotFound(keyA, null));

                r.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster)));
                r.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster)));
                r.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster)));

                ExpectMsg(new UpdateSuccess(keyA, null));
                ExpectMsg(new UpdateSuccess(keyA, null));
                ExpectMsg(new UpdateSuccess(keyA, null));

                Watch(r);
                Sys.Stop(r);
                ExpectTerminated(r);

                var r2 = default(IActorRef);
                AwaitAssert(() => r2 = NewReplicator(Sys)); // try until name is free

                // note that it will stash the commands until loading completed
                r2.Tell(Dsl.Get(keyA, ReadLocal.Instance));
                ExpectMsg<GetSuccess>().Get(keyA).Value.ShouldBe(3);

                Watch(r2);
                Sys.Stop(r2);
                ExpectTerminated(r2);

            }, first);

            EnterBarrierAfterTestStep();
        }

        public void Durable_CRDT_should_work_in_a_multi_node_cluster()
        {
            Join(second, first);

            var r = NewReplicator(Sys);
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    r.Tell(Dsl.GetReplicaCount);
                    ExpectMsg(new ReplicaCount(2));
                });
            });

            EnterBarrier("both-initialized");

            r.Tell(Dsl.Update(keyA, GCounter.Empty, writeTwo, c => c.Increment(cluster)));
            ExpectMsg(new UpdateSuccess(keyA, null));

            r.Tell(Dsl.Update(keyC, ORSet<string>.Empty, writeTwo, c => c.Add(cluster, Myself.Name)));
            ExpectMsg(new UpdateSuccess(keyC, null));

            EnterBarrier("update-done-" + testStepCounter);

            r.Tell(Dsl.Get(keyA, readTwo));
            ExpectMsg<GetSuccess>().Get(keyA).Value.ShouldBe(2);

            r.Tell(Dsl.Get(keyC, readTwo));
            ExpectMsg<GetSuccess>().Get(keyC).Elements.ShouldBe(ImmutableHashSet.CreateRange(new[] { first.Name, second.Name }));

            EnterBarrier("values-verified-" + testStepCounter);

            Watch(r);
            Sys.Stop(r);
            ExpectTerminated(r);

            var r2 = default(IActorRef);
            AwaitAssert(() => r2 = NewReplicator(Sys)); // try until name is free
            AwaitAssert(() =>
            {
                r2.Tell(Dsl.GetKeyIds);
                ExpectMsg<GetKeysIdsResult>().Keys.ShouldNotBe(ImmutableHashSet<string>.Empty);
            });

            r2.Tell(Dsl.Get(keyA, ReadLocal.Instance));
            ExpectMsg<GetSuccess>().Get(keyA).Value.ShouldBe(2);

            r2.Tell(Dsl.Get(keyC, ReadLocal.Instance));
            ExpectMsg<GetSuccess>().Get(keyC).Elements.ShouldBe(ImmutableHashSet.CreateRange(new[] { first.Name, second.Name }));

            EnterBarrierAfterTestStep();
        }

        public void Durable_CRDT_should_be_durable_after_gossip_update()
        {
            var r = NewReplicator(Sys);

            RunOn(() =>
            {
                r.Tell(Dsl.Update(keyC, ORSet<string>.Empty, WriteLocal.Instance, c => c.Add(cluster, Myself.Name)));
                ExpectMsg(new UpdateSuccess(keyC, null));
            }, first);

            RunOn(() =>
            {
                r.Tell(Dsl.Subscribe(keyC, TestActor));
                ExpectMsg<Changed>().Get(keyC).Elements.ShouldBe(ImmutableHashSet.Create(first.Name));

                // must do one more roundtrip to be sure that it keyB is stored, since Changed might have
                // been sent out before storage
                r.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster)));
                ExpectMsg(new UpdateSuccess(keyA, null));

                Watch(r);
                Sys.Stop(r);
                ExpectTerminated(r);

                var r2 = default(IActorRef);
                AwaitAssert(() => r2 = NewReplicator(Sys));
                AwaitAssert(() =>
                {
                    r2.Tell(Dsl.GetKeyIds);
                    ExpectMsg<GetKeysIdsResult>().Keys.ShouldNotBe(ImmutableHashSet<string>.Empty);
                });

                r2.Tell(Dsl.Get(keyC, ReadLocal.Instance));
                ExpectMsg<GetSuccess>().Get(keyC).Elements.ShouldBe(ImmutableHashSet.Create(first.Name));

            }, second);

            EnterBarrierAfterTestStep();
        }

        public void Durable_CRDT_should_handle_Update_before_Load()
        {
            RunOn(() =>
            {
                var sys1 = ActorSystem.Create("AdditionalSys", Sys.Settings.Config);
                var cluster1 = Akka.Cluster.Cluster.Get(sys1);
                var addr = cluster1.SelfAddress;
                try
                {
                    cluster1.Join(addr);
                    /* new TestKit(sys1) with ImplicitSender */
                    {
                        var r = NewReplicator(sys1);
                        Within(TimeSpan.FromSeconds(10), () =>
                        {
                            AwaitAssert(() =>
                            {
                                r.Tell(Dsl.GetReplicaCount);
                                ExpectMsg(new ReplicaCount(1));
                            });
                        });

                        r.Tell(Dsl.Get(keyA, ReadLocal.Instance));
                        ExpectMsg(new NotFound(keyA, null));

                        r.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster1)));
                        r.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster1)));
                        r.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster1)));
                        r.Tell(Dsl.Update(keyB, GCounter.Empty, WriteLocal.Instance, c => c.Increment(cluster1)));

                        ExpectMsg(new UpdateSuccess(keyA, null));
                        ExpectMsg(new UpdateSuccess(keyA, null));
                        ExpectMsg(new UpdateSuccess(keyA, null));
                        ExpectMsg(new UpdateSuccess(keyB, null));

                        Watch(r);
                        sys1.Stop(r);
                        ExpectTerminated(r);
                    }
                }
                finally
                {
                    sys1.Terminate().Wait(TimeSpan.FromSeconds(10));
                }

                var sys2 = ActorSystem.Create("AdditionalSys", Sys.Settings.Config);
                try
                {
                    Akka.Cluster.Cluster.Get(sys2).Join(addr);
                    /* new TestKit(sys1) with ImplicitSender */
                    {
                        var r2 = NewReplicator(sys2);

                        // it should be possible to update while loading is in progress
                        r2.Tell(Dsl.Update(keyB, GCounter.Empty, WriteLocal.Instance, c => c.Increment(Akka.Cluster.Cluster.Get(sys2))));
                        ExpectMsg(new UpdateSuccess(keyB, null));

                        // wait until all loaded
                        AwaitAssert(() =>
                        {
                            r2.Tell(Dsl.GetKeyIds);
                            ExpectMsg<GetKeysIdsResult>().Keys.ShouldBe(ImmutableHashSet.CreateRange(new [] { keyA.Id, keyB.Id }));   
                        });

                        r2.Tell(Dsl.Get(keyA, ReadLocal.Instance));
                        ExpectMsg<GetSuccess>().Get(keyA).Value.ShouldBe(3);

                        r2.Tell(Dsl.Get(keyB, ReadLocal.Instance));
                        ExpectMsg<GetSuccess>().Get(keyB).Value.ShouldBe(2);
                    }
                }
                finally
                {
                    sys1.Terminate().Wait(TimeSpan.FromSeconds(10));
                }

            }, first);
            EnterBarrierAfterTestStep();
        }

        public void Durable_CRDT_should_stop_Replicator_if_Load_fails()
        {
            RunOn(() =>
            {
                var r = Sys.ActorOf(Replicator.Props(
                            ReplicatorSettings.Create(Sys).WithDurableStoreProps(TestDurableStoreProps(failLoad: true))),
                            "replicator-" + testStepCounter);
                Watch(r);
                ExpectTerminated(r);

            }, first);
            EnterBarrierAfterTestStep();
        }

        public void Durable_CRDT_should_reply_with_StoreFailure_if_store_fails()
        {
            RunOn(() =>
            {
                var r = Sys.ActorOf(Replicator.Props(
                            ReplicatorSettings.Create(Sys).WithDurableStoreProps(TestDurableStoreProps(failStore: true))),
                            "replicator-" + testStepCounter);

                r.Tell(Dsl.Update(keyA, GCounter.Empty, WriteLocal.Instance, "a", c => c.Increment(cluster)));
                ExpectMsg(new StoreFailure(keyA, "a"));
            }, first);
            EnterBarrierAfterTestStep();
        }

        private void EnterBarrierAfterTestStep()
        {
            testStepCounter++;
            EnterBarrier("after-" + testStepCounter);
        }

        private IActorRef NewReplicator(ActorSystem system)
        {
            return system.ActorOf(Replicator.Props(
                    ReplicatorSettings.Create(system).WithGossipInterval(TimeSpan.FromSeconds(1))),
                "replicator-" + testStepCounter);
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                cluster.Join(Node(to).Address);
            }, from);
            EnterBarrier(from.Name + "-joined");
        }
    }

    public class DurableDataSpecNode1 : DurableDataSpec
    {
        public DurableDataSpecNode1() : base(new DurableDataSpecConfig(writeBehind: false)) { }
    }

    public class DurableDataWriteBehindSpecNode1 : DurableDataSpec
    {
        public DurableDataWriteBehindSpecNode1() : base(new DurableDataSpecConfig(writeBehind: true)) { }
    }
}
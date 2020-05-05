//-----------------------------------------------------------------------
// <copyright file="ClusterDeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class ClusterDeathWatchSpecConfig : MultiNodeConfig
    {
        readonly RoleName _first;
        public RoleName First { get { return _first; } }
        readonly RoleName _second;
        public RoleName Second { get { return _second; } }
        readonly RoleName _third;
        public RoleName Third { get { return _third; } }
        readonly RoleName _fourth;
        public RoleName Fourth { get { return _fourth; } }
        readonly RoleName _fifth;
        public RoleName Fifth { get { return _fifth; } }

        public ClusterDeathWatchSpecConfig()
        {
            _first = Role("first");
            _second = Role("second");
            _third = Role("third");
            _fourth = Role("fourth");
            _fifth = Role("fifth");
            DeployOn(_fourth, @"/hello.remote = ""@first@""");
            CommonConfig = ConfigurationFactory.ParseString(@"akka.cluster.publish-stats-interval = 25s
                akka.actor.debug.lifecycle = true")
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(DebugConfig(true))
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class ClusterDeathWatchSpec : MultiNodeClusterSpec
    {
        readonly ClusterDeathWatchSpecConfig _config;

        public ClusterDeathWatchSpec()
            : this(new ClusterDeathWatchSpecConfig())
        {
        }

        private ClusterDeathWatchSpec(ClusterDeathWatchSpecConfig config)
            : base(config, typeof(ClusterDeathWatchSpec))
        {
            _config = config;
        }

        private IActorRef _remoteWatcher;

        protected IActorRef RemoteWatcher
        {
            get
            {
                if (_remoteWatcher == null)
                {
                    Sys.ActorSelection("/system/remote-watcher").Tell(new Identify(null), TestActor);
                    _remoteWatcher = ExpectMsg<ActorIdentity>().Subject;
                }
                return _remoteWatcher;
            }
        }

        protected override void AtStartup()
        {
            if (!Log.IsDebugEnabled)
            {
                MuteMarkingAsUnreachable();
            }
            base.AtStartup();
        }



        [MultiNodeFact]
        public void ClusterDeathWatchSpecTests()
        {
            An_actor_watching_a_remote_actor_in_the_cluster_must_receive_terminated_when_watched_node_becomes_down_removed();
            //AnActorWatchingARemoteActorInTheClusterMustReceiveTerminatedWhenWatchedPathDoesNotExist();
            An_actor_watching_a_remote_actor_in_the_cluster_must_be_able_to_watch_actor_before_node_joins_cluster_and_cluster_remote_watcher_takes_over_from_remote_watcher();
            An_actor_watching_a_remote_actor_in_the_cluster_must_be_able_to_shutdown_system_when_using_remote_deployed_actor_on_node_that_crashed();
        }

        public void An_actor_watching_a_remote_actor_in_the_cluster_must_receive_terminated_when_watched_node_becomes_down_removed()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Fourth);
                EnterBarrier("cluster-up");

                RunOn(() =>
                {
                    EnterBarrier("subjected-started");

                    var path2 = new RootActorPath(GetAddress(_config.Second)) / "user" / "subject";
                    var path3 = new RootActorPath(GetAddress(_config.Third)) / "user" / "subject";
                    var watchEstablished = new TestLatch(2);
                    Sys.ActorOf(Props.Create(() => new Observer(path2, path3, watchEstablished, TestActor))
                        .WithDeploy(Deploy.Local), "observer1");

                    watchEstablished.Ready();
                    EnterBarrier("watch-established");
                    ExpectMsg(path2);
                    ExpectNoMsg(TimeSpan.FromSeconds(2));
                    EnterBarrier("second-terminated");
                    MarkNodeAsUnavailable(GetAddress(_config.Third));
                    AwaitAssert(() => Assert.Contains(GetAddress(_config.Third), ClusterView.UnreachableMembers.Select(x => x.Address)));
                    Cluster.Down(GetAddress(_config.Third));
                    //removed
                    AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.Third), ClusterView.Members.Select(x => x.Address)));
                    AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.Third), ClusterView.UnreachableMembers.Select(x => x.Address)));
                    ExpectMsg(path3);
                    EnterBarrier("third-terminated");
                }, _config.First);

                RunOn(() =>
                {
                    Sys.ActorOf(BlackHoleActor.Props, "subject");
                    EnterBarrier("subjected-started");
                    EnterBarrier("watch-established");
                    RunOn(() =>
                    {
                        MarkNodeAsUnavailable(GetAddress(_config.Second));
                        AwaitAssert(() => Assert.Contains(GetAddress(_config.Second), ClusterView.UnreachableMembers.Select(x => x.Address)));
                        Cluster.Down(GetAddress(_config.Second));
                        //removed
                        AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.Second), ClusterView.Members.Select(x => x.Address)));
                        AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.Second), ClusterView.UnreachableMembers.Select(x => x.Address)));
                    }, _config.Third);
                    EnterBarrier("second-terminated");
                    EnterBarrier("third-terminated");
                }, _config.Second, _config.Third, _config.Fourth);

                RunOn(() =>
                {
                    EnterBarrier("subjected-started");
                    EnterBarrier("watch-established");
                    EnterBarrier("second-terminated");
                    EnterBarrier("third-terminated");
                }, _config.Fifth);

                EnterBarrier("after-1");
            });
        }

        /* 
         * NOTE: it's not possible to watch a path that doesn't exist in Akka.NET
         * REASON: in order to do this, you fist need an ActorRef. Can't get one for
         * a path that doesn't exist at the time of creation. In Scala Akka they have to use
         * System.ActorFor for this, which has been deprecated for a long time and has never been
         * supported in Akka.NET.
        */
        //public void AnActorWatchingARemoteActorInTheClusterMustReceiveTerminatedWhenWatchedPathDoesNotExist()
        //{
        //    Thread.Sleep(5000);
        //    RunOn(() =>
        //    {
        //        var path2 = new RootActorPath(GetAddress(_config.Second)) / "user" / "non-existing";
        //        Sys.ActorOf(Props.Create(() => new DumbObserver(path2, TestActor)).WithDeploy(Deploy.Local), "observer3");
        //        ExpectMsg(path2);
        //    }, _config.First);

        //    EnterBarrier("after-2");
        //}

        public void An_actor_watching_a_remote_actor_in_the_cluster_must_be_able_to_watch_actor_before_node_joins_cluster_and_cluster_remote_watcher_takes_over_from_remote_watcher()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() => Sys.ActorOf(BlackHoleActor.Props.WithDeploy(Deploy.Local), "subject5"), _config.Fifth);
                EnterBarrier("subjected-started");

                RunOn(() =>
                {
                    Sys.ActorSelection(new RootActorPath(GetAddress(_config.Fifth)) / "user" / "subject5").Tell(new Identify("subject5"), TestActor);
                    var subject5 = ExpectMsg<ActorIdentity>().Subject;
                    Watch(subject5);

                    //fifth is not a cluster member, so the watch is handled by the RemoteWatcher
                    AwaitAssert(() =>
                    {
                        RemoteWatcher.Tell(Remote.RemoteWatcher.Stats.Empty);
                        var stats = ExpectMsg<Remote.RemoteWatcher.Stats>();
                        stats.WatchingRefs.Contains((subject5, TestActor)).ShouldBeTrue();
                        stats.WatchingAddresses.Contains(GetAddress(_config.Fifth)).ShouldBeTrue();
                    });
                }, _config.First);
                EnterBarrier("remote-watch");

                // second and third are already removed
                AwaitClusterUp(_config.First, _config.Fourth, _config.Fifth);

                RunOn(() =>
                {
                    // fifth is member, so the watch is handled by the ClusterRemoteWatcher,
                    // and cleaned up from RemoteWatcher
                    AwaitAssert(() =>
                    {
                        RemoteWatcher.Tell(Remote.RemoteWatcher.Stats.Empty);
                        var stats = ExpectMsg<Remote.RemoteWatcher.Stats>();
                        stats.WatchingRefs.Select(x => x.Item1.Path.Name).Contains("subject5").ShouldBeTrue();
                        stats.WatchingAddresses.Contains(GetAddress(_config.Fifth)).ShouldBeFalse();
                    });
                }, _config.First);

                EnterBarrier("cluster-watch");

                RunOn(() =>
                {
                    MarkNodeAsUnavailable(GetAddress(_config.Fifth));
                    AwaitAssert(() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(GetAddress(_config.Fifth)).ShouldBeTrue());
                    Cluster.Down(GetAddress(_config.Fifth));
                    // removed
                    AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.Fifth), ClusterView.UnreachableMembers.Select(x => x.Address)));
                    AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.Fifth), ClusterView.Members.Select(x => x.Address)));
                }, _config.Fourth);

                EnterBarrier("fifth-terminated");
                RunOn(() =>
                {
                    ExpectMsg<Terminated>().ActorRef.Path.Name.ShouldBe("subject5");
                }, _config.First);

                EnterBarrier("after-3");
            });

        }

        public void An_actor_watching_a_remote_actor_in_the_cluster_must_be_able_to_shutdown_system_when_using_remote_deployed_actor_on_node_that_crashed()
        {
            Within(TimeSpan.FromSeconds(20), () =>
            {
                // fourth actor system will be shutdown, not part of testConductor any more
                // so we can't use barriers to synchronize with it
                var firstAddress = GetAddress(_config.First);
                RunOn(() =>
                {
                    Sys.ActorOf(Props.Create(() => new EndActor(TestActor, null)), "end");
                }, _config.First);
                EnterBarrier("end-actor-created");

                RunOn(() =>
                {
                    var hello = Sys.ActorOf(BlackHoleActor.Props, "hello");
                    Assert.IsType<RemoteActorRef>(hello);
                    hello.Path.Address.ShouldBe(GetAddress(_config.First));
                    Watch(hello);
                    EnterBarrier("hello-deployed");
                    MarkNodeAsUnavailable(GetAddress(_config.First));
                    AwaitAssert(() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(GetAddress(_config.First)).ShouldBeTrue());
                    Cluster.Down(GetAddress(_config.First));
                    // removed
                    AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.First), ClusterView.UnreachableMembers.Select(x => x.Address)));
                    AwaitAssert(() => Assert.DoesNotContain(GetAddress(_config.First), ClusterView.Members.Select(x => x.Address)));

                    ExpectTerminated(hello);
                    EnterBarrier("first-unavailable");

                    var timeout = RemainingOrDefault;
                    try
                    {
                        if (!Sys.WhenTerminated.Wait(timeout)) // TestConductor.Shutdown called by First MUST terminate this actor system
                        {
                            Assert.True(false, String.Format("Failed to stop [{0}] within [{1}]", Sys.Name, timeout));
                        }
                    }
                    catch (TimeoutException)
                    {
                        Assert.True(false, String.Format("Failed to stop [{0}] within [{1}]", Sys.Name, timeout));
                    }

                    
                    // signal to the first node that the fourth node is done
                    var endSystem = ActorSystem.Create("EndSystem", Sys.Settings.Config);
                    try
                    {
                        var endProbe = CreateTestProbe(endSystem);
                        var endActor = endSystem.ActorOf(Props.Create(() => new EndActor(endProbe.Ref, firstAddress)),
                            "end");
                        endActor.Tell(EndActor.SendEnd.Instance);
                        endProbe.ExpectMsg<EndActor.EndAck>();
                    }
                    finally
                    {
                        Shutdown(endSystem, TimeSpan.FromSeconds(10));
                    }

                    // no barrier here, because it is not part of TestConductor roles any more

                }, _config.Fourth);

                RunOn(() =>
                {
                    EnterBarrier("hello-deployed");
                    EnterBarrier("first-unavailable");

                    // don't end the test until fourth is done
                    RunOn(() =>
                    {
                        // fourth system will be shutdown, remove to not participate in barriers any more
                        TestConductor.Shutdown(_config.Fourth).Wait();
                        ExpectMsg<EndActor.End>();
                    }, _config.First);

                    EnterBarrier("after-4");
                }, _config.First, _config.Second, _config.Third, _config.Fifth);

            });
        }

        /// <summary>
        /// Used to report <see cref="Terminated"/> events to the <see cref="TestActor"/>
        /// </summary>
        class Observer : ReceiveActor
        {
            private readonly IActorRef _testActorRef;
            readonly TestLatch _watchEstablished;

            public Observer(ActorPath path2, ActorPath path3, TestLatch watchEstablished, IActorRef testActorRef)
            {
                _watchEstablished = watchEstablished;
                _testActorRef = testActorRef;

                Receive<ActorIdentity>(identity => identity.MessageId.Equals(path2), identity =>
                {
                    Context.Watch(identity.Subject);
                    _watchEstablished.CountDown();
                });

                Receive<ActorIdentity>(identity => identity.MessageId.Equals(path3), identity =>
                {
                    Context.Watch(identity.Subject);
                    _watchEstablished.CountDown();
                });

                Receive<Terminated>(terminated =>
                {
                    _testActorRef.Tell(terminated.ActorRef.Path);
                });

                Context.ActorSelection(path2).Tell(new Identify(path2));
                Context.ActorSelection(path3).Tell(new Identify(path3));

            }
        }

        class DumbObserver : ReceiveActor
        {
            private readonly IActorRef _testActorRef;

            public DumbObserver(ActorPath path2, IActorRef testActorRef)
            {
                _testActorRef = testActorRef;

                Receive<ActorIdentity>(identity =>
                {
                    Context.Watch(identity.Subject);
                });

                Receive<Terminated>(terminated =>
                {
                    _testActorRef.Tell(terminated.ActorRef.Path);
                });

                Context.ActorSelection(path2).Tell(new Identify(path2));
            }
        }
    }
}


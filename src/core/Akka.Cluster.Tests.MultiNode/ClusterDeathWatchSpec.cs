//-----------------------------------------------------------------------
// <copyright file="ClusterDeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode;

public class ClusterDeathWatchSpecConfig : MultiNodeConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }
    public RoleName Fourth { get; }
    public RoleName Fifth { get; }

    public ClusterDeathWatchSpecConfig()
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");
        Fourth = Role("fourth");
        Fifth = Role("fifth");
        DeployOn(Fourth, @"/hello.remote = ""@first@""");
        CommonConfig = ConfigurationFactory.ParseString(@"akka.cluster.publish-stats-interval = 25s
                akka.actor.debug.lifecycle = true")
            .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
            .WithFallback(DebugConfig(true))
            .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
    }
}

public class ClusterDeathWatchSpec : MultiNodeClusterSpec
{
    private readonly ClusterDeathWatchSpecConfig _config;

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
    public async Task ClusterDeathWatchSpecTests()
    {
        await An_actor_watching_a_remote_actor_in_the_cluster_must_receive_terminated_when_watched_node_becomes_down_removed();
        //AnActorWatchingARemoteActorInTheClusterMustReceiveTerminatedWhenWatchedPathDoesNotExist();
        await An_actor_watching_a_remote_actor_in_the_cluster_must_be_able_to_watch_actor_before_node_joins_cluster_and_cluster_remote_watcher_takes_over_from_remote_watcher();
        await An_actor_watching_a_remote_actor_in_the_cluster_must_be_able_to_shutdown_system_when_using_remote_deployed_actor_on_node_that_crashed();
    }

    public async Task An_actor_watching_a_remote_actor_in_the_cluster_must_receive_terminated_when_watched_node_becomes_down_removed()
    {
        await WithinAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second, _config.Third, _config.Fourth);
            await EnterBarrierAsync("cluster-up");

            await RunOnAsync(async () =>
            {
                await EnterBarrierAsync("subjected-started");

                var path2 = new RootActorPath(GetAddress(_config.Second)) / "user" / "subject";
                var path3 = new RootActorPath(GetAddress(_config.Third)) / "user" / "subject";
                var watchEstablished = new TestLatch(2);
                Sys.ActorOf(Props.Create(() => new Observer(path2, path3, watchEstablished, TestActor))
                    .WithDeploy(Deploy.Local), "observer1");

                watchEstablished.Ready();
                await EnterBarrierAsync("watch-established");
                await ExpectMsgAsync(path2);
                await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
                await EnterBarrierAsync("second-terminated");
                MarkNodeAsUnavailable(GetAddress(_config.Third));
                await AwaitAssertAsync(() => Assert.Contains(GetAddress(_config.Third), ClusterView.UnreachableMembers.Select(x => x.Address)));
                Cluster.Down(GetAddress(_config.Third));
                //removed
                await AwaitAssertAsync(() => Assert.DoesNotContain(GetAddress(_config.Third), ClusterView.Members.Select(x => x.Address)));
                await AwaitAssertAsync(() => Assert.DoesNotContain(GetAddress(_config.Third), ClusterView.UnreachableMembers.Select(x => x.Address)));
                await ExpectMsgAsync(path3);
                await EnterBarrierAsync("third-terminated");
            }, _config.First);

            await RunOnAsync(async () =>
            {
                Sys.ActorOf(BlackHoleActor.Props, "subject");
                await EnterBarrierAsync("subjected-started");
                await EnterBarrierAsync("watch-established");
                await RunOnAsync(async () =>
                {
                    MarkNodeAsUnavailable(GetAddress(_config.Second));
                    await AwaitAssertAsync(() => Assert.Contains(GetAddress(_config.Second), ClusterView.UnreachableMembers.Select(x => x.Address)));
                    Cluster.Down(GetAddress(_config.Second));
                    //removed
                    await AwaitAssertAsync(() => Assert.DoesNotContain(GetAddress(_config.Second), ClusterView.Members.Select(x => x.Address)));
                    await AwaitAssertAsync(() => Assert.DoesNotContain(GetAddress(_config.Second), ClusterView.UnreachableMembers.Select(x => x.Address)));
                }, _config.Third);
                await EnterBarrierAsync("second-terminated");
                await EnterBarrierAsync("third-terminated");
            }, _config.Second, _config.Third, _config.Fourth);

            await RunOnAsync(async () =>
            {
                await EnterBarrierAsync("subjected-started");
                await EnterBarrierAsync("watch-established");
                await EnterBarrierAsync("second-terminated");
                await EnterBarrierAsync("third-terminated");
            }, _config.Fifth);

            await EnterBarrierAsync("after-1");
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

    public async Task An_actor_watching_a_remote_actor_in_the_cluster_must_be_able_to_watch_actor_before_node_joins_cluster_and_cluster_remote_watcher_takes_over_from_remote_watcher()
    {
        await WithinAsync(TimeSpan.FromSeconds(20), async () =>
        {
            RunOn(() => Sys.ActorOf(BlackHoleActor.Props.WithDeploy(Deploy.Local), "subject5"), _config.Fifth);
            await EnterBarrierAsync("subjected-started");

            await RunOnAsync(async () =>
            {
                Sys.ActorSelection(new RootActorPath(GetAddress(_config.Fifth)) / "user" / "subject5").Tell(new Identify("subject5"), TestActor);
                var subject5 = (await ExpectMsgAsync<ActorIdentity>()).Subject;
                await WatchAsync(subject5);

                //fifth is not a cluster member, so the watch is handled by the RemoteWatcher
                await AwaitAssertAsync(async () =>
                {
                    RemoteWatcher.Tell(Remote.RemoteWatcher.Stats.Empty);
                    var stats = await ExpectMsgAsync<Remote.RemoteWatcher.Stats>();
                    stats.WatchingRefs.Contains((subject5, TestActor)).ShouldBeTrue();
                    stats.WatchingAddresses.Contains(GetAddress(_config.Fifth)).ShouldBeTrue();
                });
            }, _config.First);
            await EnterBarrierAsync("remote-watch");

            // second and third are already removed
            await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Fourth, _config.Fifth);

            await RunOnAsync(async () =>
            {
                // fifth is member, so the watch is handled by the ClusterRemoteWatcher,
                // and cleaned up from RemoteWatcher
                await AwaitAssertAsync(async () =>
                {
                    RemoteWatcher.Tell(Remote.RemoteWatcher.Stats.Empty);
                    var stats = await ExpectMsgAsync<Remote.RemoteWatcher.Stats>();
                    stats.WatchingRefs.Select(x => x.Item1.Path.Name).Contains("subject5").ShouldBeTrue();
                    stats.WatchingAddresses.Contains(GetAddress(_config.Fifth)).ShouldBeFalse();
                });
            }, _config.First);

            await EnterBarrierAsync("cluster-watch");

            await RunOnAsync(async () =>
            {
                MarkNodeAsUnavailable(GetAddress(_config.Fifth));
                await AwaitAssertAsync(() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(GetAddress(_config.Fifth)).ShouldBeTrue());
                Cluster.Down(GetAddress(_config.Fifth));
                // removed
                await AwaitAssertAsync(() => Assert.DoesNotContain(GetAddress(_config.Fifth), ClusterView.UnreachableMembers.Select(x => x.Address)));
                await AwaitAssertAsync(() => Assert.DoesNotContain(GetAddress(_config.Fifth), ClusterView.Members.Select(x => x.Address)));
            }, _config.Fourth);

            await EnterBarrierAsync("fifth-terminated");
            await RunOnAsync(async () =>
            {
                (await ExpectMsgAsync<Terminated>()).ActorRef.Path.Name.ShouldBe("subject5");
            }, _config.First);

            await EnterBarrierAsync("after-3");
        });

    }

    public async Task An_actor_watching_a_remote_actor_in_the_cluster_must_be_able_to_shutdown_system_when_using_remote_deployed_actor_on_node_that_crashed()
    {
        await WithinAsync(TimeSpan.FromSeconds(20), async () =>
        {
            // fourth actor system will be shutdown, not part of testConductor any more
            // so we can't use barriers to synchronize with it
            var firstAddress = GetAddress(_config.First);
            RunOn(() =>
            {
                Sys.ActorOf(Props.Create(() => new EndActor(TestActor, null)), "end");
            }, _config.First);
            await EnterBarrierAsync("end-actor-created");

            await RunOnAsync(async () =>
            {
                var hello = Sys.ActorOf(BlackHoleActor.Props, "hello");
                Assert.IsType<RemoteActorRef>(hello);
                hello.Path.Address.ShouldBe(GetAddress(_config.First));
                await WatchAsync(hello);
                await EnterBarrierAsync("hello-deployed");
                MarkNodeAsUnavailable(GetAddress(_config.First));
                await AwaitAssertAsync(() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(GetAddress(_config.First)).ShouldBeTrue());
                Cluster.Down(GetAddress(_config.First));
                // removed
                await AwaitAssertAsync(() => Assert.DoesNotContain(GetAddress(_config.First), ClusterView.UnreachableMembers.Select(x => x.Address)));
                await AwaitAssertAsync(() => Assert.DoesNotContain(GetAddress(_config.First), ClusterView.Members.Select(x => x.Address)));

                await ExpectTerminatedAsync(hello);
                await EnterBarrierAsync("first-unavailable");

                var timeout = RemainingOrDefault;
                try
                {
                    // TestConductor.Shutdown called by First MUST terminate this actor system
                    await Sys.WhenTerminated.WaitAsync(timeout);
                }
                catch (Exception)
                {
                    Assert.Fail($"Failed to stop [{Sys.Name}] within [{timeout}]");
                }

                    
                // signal to the first node that the fourth node is done
                var endSystem = ActorSystem.Create("EndSystem", Sys.Settings.Config);
                try
                {
                    var endProbe = CreateTestProbe(endSystem);
                    var endActor = endSystem.ActorOf(Props.Create(() => new EndActor(endProbe.Ref, firstAddress)),
                        "end");
                    endActor.Tell(EndActor.SendEnd.Instance);
                    await endProbe.ExpectMsgAsync<EndActor.EndAck>();
                }
                finally
                {
                    Shutdown(endSystem, TimeSpan.FromSeconds(10));
                }

                // no barrier here, because it is not part of TestConductor roles any more

            }, _config.Fourth);

            await RunOnAsync(async () =>
            {
                await EnterBarrierAsync("hello-deployed");
                await EnterBarrierAsync("first-unavailable");

                // don't end the test until fourth is done
                await RunOnAsync(async () =>
                {
                    // fourth system will be shutdown, remove to not participate in barriers any more
                    await TestConductor.ShutdownAsync(_config.Fourth);
                    await ExpectMsgAsync<EndActor.End>();
                }, _config.First);

                await EnterBarrierAsync("after-4");
            }, _config.First, _config.Second, _config.Third, _config.Fifth);

        });
    }

    /// <summary>
    /// Used to report <see cref="Terminated"/> events to the <see cref="TestActor"/>
    /// </summary>
    private class Observer : ReceiveActor
    {
        private readonly IActorRef _testActorRef;
        private readonly TestLatch _watchEstablished;

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
}
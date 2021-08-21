// //-----------------------------------------------------------------------
// // <copyright file="ReplicatorResiliencySpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.DistributedData.Durable;
using Akka.DistributedData.LightningDB;
using Akka.Pattern;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    public class ReplicatorResiliencySpec : AkkaSpec
    {
        public static readonly Config SpecConfig;

        private readonly ActorSystem _sys1;
        private readonly ActorSystem _sys2;
        private readonly ActorSystem _sys3;

        private readonly IActorRef _replicator1;
        private readonly IActorRef _replicator2;
        private readonly IActorRef _replicator3;

        static ReplicatorResiliencySpec()
        {
            SpecConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.distributed-data.durable.keys = [""*""]
                akka.cluster.distributed-data.durable.store-actor-class = ""Akka.DistributedData.Tests.FakeDurableStore, Akka.DistributedData.Tests")
                .WithFallback(DistributedData.DefaultConfig());
        }

        public ReplicatorResiliencySpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            _sys1 = Sys;
            _sys3 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);

            var settings = ReplicatorSettings.Create(Sys)
                .WithGossipInterval(TimeSpan.FromSeconds(1.0))
                .WithMaxDeltaElements(10)
                .WithRestartReplicatorOnFailure(true);

            var props = BackoffSupervisor.Props(
                    Backoff.OnStop(
                            childProps: Replicator.Props(settings),
                            childName: "replicator",
                            minBackoff: TimeSpan.FromSeconds(3),
                            maxBackoff: TimeSpan.FromSeconds(300),
                            randomFactor: 0.2,
                            maxNrOfRetries: -1)
                        .WithFinalStopMessage(m => m is Terminate))
                .WithDeploy(Deploy.Local).WithDispatcher(settings.Dispatcher);

            _replicator1 = _sys1.ActorOf(props, "replicatorSuper");
            _replicator2 = _sys2.ActorOf(props, "replicatorSuper");
            _replicator3 = _sys3.ActorOf(props, "replicatorSuper");


        }

        private async Task InitCluster()
        {
            Cluster.Cluster.Get(_sys1).Join(Cluster.Cluster.Get(_sys1).SelfAddress); // coordinator will initially run on sys1
            await AwaitAssertAsync(() => Cluster.Cluster.Get(_sys1).SelfMember.Status.Should().Be(MemberStatus.Up));

            Cluster.Cluster.Get(_sys2).Join(Cluster.Cluster.Get(_sys1).SelfAddress);
            await WithinAsync(10.Seconds(), async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    Cluster.Cluster.Get(_sys1).State.Members.Count.Should().Be(2);
                    Cluster.Cluster.Get(_sys1).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Cluster.Get(_sys2).State.Members.Count.Should().Be(2);
                    Cluster.Cluster.Get(_sys2).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                });
            });

            Cluster.Cluster.Get(_sys3).Join(Cluster.Cluster.Get(_sys1).SelfAddress);
            await WithinAsync(10.Seconds(), async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    Cluster.Cluster.Get(_sys1).State.Members.Count.Should().Be(3);
                    Cluster.Cluster.Get(_sys1).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Cluster.Get(_sys2).State.Members.Count.Should().Be(3);
                    Cluster.Cluster.Get(_sys2).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Cluster.Get(_sys3).State.Members.Count.Should().Be(3);
                    Cluster.Cluster.Get(_sys3).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                });
            });
        }

        [Fact]
        public async Task Handle_Durable_Store_Exception()
        {
            await InitCluster();
            await DurableStoreActorCrash();
        }

        public async Task DurableStoreActorCrash()
        {
            const string replicatorActorPath = "/user/replicatorSuper/replicator";
            const string durableStoreActorPath = "/user/replicatorSuper/replicator/durableStore";

            var durableStore = _sys1.ActorSelection(durableStoreActorPath).ResolveOne(TimeSpan.FromSeconds(3)).ContinueWith(
                m => m.Result).Result;

            var replicator = _sys1.ActorSelection(replicatorActorPath).ResolveOne(TimeSpan.FromSeconds(3)).ContinueWith(
                m => m.Result).Result;

            Watch(replicator);
            Watch(durableStore);
            durableStore.Tell(new InitFail());

            var terminated = ExpectMsg<Terminated>(TimeSpan.FromSeconds(10));
            if (!terminated.ActorRef.Path.Equals(durableStore.Path) && !terminated.ActorRef.Path.Equals(replicator.Path))
            {
                throw new Exception(
                    $"Expecting termination of either durable storage or replicator, found {terminated.ActorRef.Path} instead.");
            }

            terminated = ExpectMsg<Terminated>(TimeSpan.FromSeconds(10));
            if (!terminated.ActorRef.Path.Equals(durableStore.Path) && !terminated.ActorRef.Path.Equals(replicator.Path))
            {
                throw new Exception(
                    $"Expecting termination of either durable storage or replicator, found {terminated.ActorRef.Path} instead.");
            }

            //The supervisor should have restarted the replicator actor by now
            await AwaitAssertAsync(async () =>
            {
                // Is the replicator actor recreated
                var newReplicator = await _sys1.ActorSelection(replicatorActorPath).ResolveOne(TimeSpan.FromSeconds(5)).ContinueWith(
                    m => m.Result);

                // We should be able to identify the recreated actor to prove the actor exists
                await newReplicator.Ask<ActorIdentity>(new Identify(Guid.NewGuid().ToString())).ContinueWith(r =>
                {
                    Assert.Equal(replicatorActorPath,r.Result.Subject.Path.ToStringWithoutAddress());
                });
            },TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task DistributedData_Replicator_Defaults_to_NoSupervisor()
        {
            const string replicatorActorPath = "/user/ddataReplicator";
            const string durableStoreActorPath = "/user/ddataReplicator/durableStore";

            await InitCluster();
            var replicator = DistributedData.Get(_sys1).Replicator;

            IActorRef durableStore = null;
            await AwaitAssertAsync(() =>
            {
                durableStore = _sys1.ActorSelection(durableStoreActorPath).ResolveOne(TimeSpan.FromSeconds(3))
                    .ContinueWith(
                        m => m.Result).Result;
            }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));

            Watch(replicator);
            Watch(durableStore);
            durableStore.Tell(new InitFail());

            // termination orders aren't guaranteed, so can't use ExpectTerminated here
            var terminated1 = ExpectMsg<Terminated>(TimeSpan.FromSeconds(10));
            var terminated2 = ExpectMsg<Terminated>(TimeSpan.FromSeconds(10));
            ImmutableHashSet.Create(terminated1.ActorRef, terminated2.ActorRef).Should().BeEquivalentTo(durableStore, replicator);

            // The replicator should not have been recreated, so expect ActorNotFound
            await Assert.ThrowsAsync<ActorNotFoundException>(() =>
                _sys1.ActorSelection(replicatorActorPath).ResolveOne(TimeSpan.FromSeconds(5)));
        }
    }

    public class FakeDurableStore : ReceiveActor
    {
        public FakeDurableStore(Config config)
        {
            Context.System.Log.Info("FakeDurableStore Initialising");
            Receive<Store>(store => { store.Reply?.ReplyTo.Tell(store.Reply.SuccessMessage); });
            Receive<LoadAll>( load=> { Sender.Tell(LoadAllCompleted.Instance); });
            Receive<InitFail>( init => { throw new LoadFailedException("failed to load durable distributed-data"); });
        }

    }

    public class InitFail
    {
    }
}
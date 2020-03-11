//-----------------------------------------------------------------------
// <copyright file="RestartNodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class RestartNodeSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }

        public RoleName Second { get; }

        public RoleName Third { get; }

        public RestartNodeSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(true)
                .WithFallback(ConfigurationFactory.ParseString(@"akka.cluster.auto-down-unreachable-after = 5s"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class RestartNodeSpec : MultiNodeClusterSpec
    {
        readonly RestartNodeSpecConfig _config;

        volatile UniqueAddress _secondUniqueAddress;
        public UniqueAddress SecondUniqueAddress { get { return _secondUniqueAddress; } }

        ImmutableList<Address> SeedNodes => ImmutableList.Create(GetAddress(_config.First), SecondUniqueAddress.Address,
            GetAddress(_config.Third));

        // use a separate ActorSystem, to be able to simulate restart
        private Lazy<ActorSystem> _secondSystem;
        private Lazy<ActorSystem> _secondRestartedSystem;

        public RestartNodeSpec() : this(new RestartNodeSpecConfig()) { }

        protected RestartNodeSpec(RestartNodeSpecConfig config) : base(config, typeof(RestartNodeSpec))
        {
            _config = config;
            _secondSystem = new Lazy<ActorSystem>(() => ActorSystem.Create(Sys.Name, Sys.Settings.Config));
            _secondRestartedSystem = new Lazy<ActorSystem>(() => ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.port=" + _secondUniqueAddress.Address.Port)
                .WithFallback(Sys.Settings.Config)));
        }

        public class Watcher : ReceiveActor
        {
            private Address _a;
            private IActorRef _replyTo;

            public Watcher(Address a, IActorRef replyTo)
            {
                _a = a;
                _replyTo = replyTo;

                Context.ActorSelection(new RootActorPath(a) / "user" / "address-receiver").Tell(new Identify(null));

                Receive<ActorIdentity>(identity =>
                {
                    Context.Watch(identity.Subject);
                    _replyTo.Tell(Done.Instance);
                });

                Receive<Terminated>(t => { });
            }
        }

        protected override void AfterTermination()
        {
            RunOn(() =>
            {
                if(_secondSystem.Value.WhenTerminated.IsCompleted)
                    Shutdown(_secondRestartedSystem.Value);
                else
                    Shutdown(_secondSystem.Value);
            }, _config.Second);

            base.AfterTermination();
        }

        [MultiNodeFact]
        public void ClusterNodesMustBeAbleToRestartAndJoinAgain()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                // secondSystem is a separate ActorSystem, to be able to simulate restart
                // we must transfer its address to first
                RunOn(() =>
                {
                    Sys.ActorOf(c => c.Receive<UniqueAddress>((address, ctx) =>
                    {
                        _secondUniqueAddress = address;
                        ctx.Sender.Tell("ok");
                    }), "address-receiver");

                    EnterBarrier("second-address-receiver-ready");

                }, _config.First, _config.Third);

                RunOn(() =>
                {
                    EnterBarrier("second-address-receiver-ready");
                    _secondUniqueAddress = Cluster.Get(_secondSystem.Value).SelfUniqueAddress;
                    foreach (var r in new[] {GetAddress(_config.First), GetAddress(_config.Third)})
                    {
                        Sys.ActorSelection(new RootActorPath(r) / "user" / "address-receiver").Tell(_secondUniqueAddress);
                        ExpectMsg("ok", TimeSpan.FromSeconds(5));
                    }
                }, _config.Second);
                EnterBarrier("second-address-transfered");

                // now we can join first, secondSystem, third together
                RunOn(() =>
                {
                    Cluster.JoinSeedNodes(SeedNodes);
                    AwaitMembersUp(3);
                }, _config.First, _config.Third);

                RunOn(() =>
                {
                    Cluster.Get(_secondSystem.Value).JoinSeedNodes(SeedNodes);
                    AwaitAssert(() => Assert.Equal(3, Cluster.Get(_secondSystem.Value).ReadView.Members.Count));
                    AwaitAssert(() => Assert.True(Cluster.Get(_secondSystem.Value).ReadView.Members.Select(x => x.Status).All(s => s == MemberStatus.Up)));
                }, _config.Second);
                EnterBarrier("started");

                // shutdown _secondSystem
                RunOn(() =>
                {
                    // send system message just before shutdown
                    _secondSystem.Value.ActorOf(Props.Create(() => new Watcher(GetAddress(_config.First), TestActor)),
                        "testWatcher");
                    ExpectMsg<Done>();

                    Shutdown(_secondSystem.Value, Remaining);
                }, _config.Second);

                EnterBarrier("second-shutdown");

                // then immediately start restartedSecondSystem, which has the same address as secondSystem
                RunOn(() =>
                {
                    Cluster.Get(_secondRestartedSystem.Value).JoinSeedNodes(SeedNodes);
                    AwaitAssert(() => Assert.Equal(3, Cluster.Get(_secondRestartedSystem.Value).ReadView.Members.Count));
                    AwaitAssert(() => Assert.True(Cluster.Get(_secondRestartedSystem.Value).ReadView.Members.Select(x => x.Status).All(s => s == MemberStatus.Up)));
                }, _config.Second);

                RunOn(() =>
                {
                    AwaitAssert(() =>
                    {
                        Assert.Equal(3, Cluster.Get(Sys).ReadView.Members.Count);
                        Assert.Contains(
                            Cluster.Get(Sys).ReadView.Members,
                            m => m.Address.Equals(SecondUniqueAddress.Address) && m.UniqueAddress.Uid != SecondUniqueAddress.Uid);
                    });
                }, _config.First, _config.Third);

                EnterBarrier("second-restarted");
            });
          
        }
    }
}

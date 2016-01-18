//-----------------------------------------------------------------------
// <copyright file="ClusterClientSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.Tests.MultiNode;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Client
{
    public class ClusterClientSpecConfig : MultiNodeConfig
    {
        public readonly RoleName Client;
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;
        public readonly RoleName Fourth;

        public ClusterClientSpecConfig()
        {
            Client = Role("client");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s
                akka.cluster.client.heartbeat-interval = 1s
                akka.cluster.client.acceptable-heartbeat-pause = 3s
            ").WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class ClusterClientNode1 : ClusterClientSpec { }
    public class ClusterClientNode2 : ClusterClientSpec { }
    public class ClusterClientNode3 : ClusterClientSpec { }
    public class ClusterClientNode4 : ClusterClientSpec { }
    public class ClusterClientNode5 : ClusterClientSpec { }

    public abstract class ClusterClientSpec : MultiNodeClusterSpec
    {
        #region setup

        public class TestService : ReceiveActor
        {
            public TestService(IActorRef testActorRef)
            {
                ReceiveAny(msg =>
                {
                    testActorRef.Forward(msg);
                    Sender.Tell(msg.ToString() + "-ack");
                });
            }
        }

        public class Service : ReceiveActor
        {
            public Service()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        private readonly RoleName _client;
        private readonly RoleName _first;
        private readonly RoleName _second;
        private readonly RoleName _third;
        private readonly RoleName _fourth;

        private ISet<RoleName> _remainingServerRoleNames;

        private ActorPath[] InitialContacts
        {
            get
            {
                return _remainingServerRoleNames.Except(new[] { _first, _fourth }).Select(r => Node(r) / "user" / "receptionist").ToArray();
            }
        }

        protected ClusterClientSpec() : this(new ClusterClientSpecConfig())
        {
        }

        protected ClusterClientSpec(ClusterClientSpecConfig config) : base(config)
        {
            _client = config.Client;
            _first = config.First;
            _second = config.Second;
            _third = config.Third;
            _fourth = config.Fourth;

            _remainingServerRoleNames = new HashSet<RoleName>(new[] { _first, _second, _third, _fourth });
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                CreateReceptionist();
            }, from);
            EnterBarrier(to.Name + "-joined");
        }

        private void CreateReceptionist()
        {
            var x = ClusterClientReceptionist.Get(Sys);
        }

        private void AwaitCount(int expected)
        {
            AwaitAssert(() =>
            {
                DistributedPubSub.Get(Sys).Mediator.Tell(Count.Instance);
                Assert.Equal(expected, ExpectMsg<int>());
            });
        }

        private RoleName GetRoleName(Address address)
        {
            return _remainingServerRoleNames.FirstOrDefault(r => Node(r).Address.Equals(address));
        }

        #endregion

        //[MultiNodeFact(Skip = "TODO")]
        public void ClusterClient_should_startup_cluster()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                Join(_first, _first);
                Join(_second, _first);
                Join(_third, _first);
                Join(_fourth, _first);

                RunOn(() =>
                {
                    var service = Sys.ActorOf(Props.Create(() => new TestService(TestActor)), "testService");
                    ClusterClientReceptionist.Get(Sys).RegisterService(service);
                }, _fourth);

                RunOn(() =>
                {
                    AwaitCount(1);
                }, _first, _second, _third, _fourth);
                EnterBarrier("after-1");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void ClusterClient_should_communicate_to_any_node_in_cluster()
        {
            ClusterClient_should_startup_cluster();

            Within(TimeSpan.FromSeconds(10), () =>
            {
                RunOn(() =>
                {
                    var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client1");
                    c.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity: true));
                    ExpectMsg("hello-ack");
                    Sys.Stop(c);
                }, _client);

                RunOn(() =>
                {
                    ExpectMsg("hello");
                }, _fourth);

                EnterBarrier("after-2");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void ClusterClient_should_demonstrate_usage()
        {
            ClusterClient_should_communicate_to_any_node_in_cluster();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var serviceA = Sys.ActorOf(Props.Create<Service>(), "serviceA");
                    ClusterClientReceptionist.Get(Sys).RegisterService(serviceA);
                }, _first);

                RunOn(() =>
                {
                    var serviceB = Sys.ActorOf(Props.Create<Service>(), "serviceB");
                    ClusterClientReceptionist.Get(Sys).RegisterService(serviceB);
                }, _second, _third);

                RunOn(() =>
                {
                    AwaitCount(4);
                }, _first, _second, _third, _fourth);

                RunOn(() =>
                {
                    var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client");
                    c.Tell(new ClusterClient.Send("/user/serviceA", "hello", localAffinity: true));
                    c.Tell(new ClusterClient.SendToAll("/user/serviceB", "hi"));
                }, _client);

                RunOn(() =>
                {
                    // note that "hi" was sent to 2 "serviceB"
                    var received = ReceiveN(3);
                    Assert.True(received.Contains("hello"));
                    Assert.True(received.Contains("hi"));
                }, _client);

                // strange, barriers fail without this sleep
                Thread.Sleep(1000);
                EnterBarrier("after-3");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void ClusterClient_should_reestablish_connection_to_another_receptionist_when_server_is_shutdown()
        {
            ClusterClient_should_demonstrate_usage();

            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    var service2 = Sys.ActorOf(Props.Create(() => new TestService(TestActor)), "service2");
                    ClusterClientReceptionist.Get(Sys).RegisterService(service2);
                    AwaitCount(8);
                }, _first, _second, _third, _fourth);
                EnterBarrier("service2-replicated");

                RunOn(() =>
                {
                    var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client2");
                    c.Tell(new ClusterClient.Send("/user/service2", "bonjour", localAffinity: true));
                    ExpectMsg("bonjour-ack");
                    var lastSenderAddress = LastSender.Path.Address;

                    var receptionistRoleName = RoleName(lastSenderAddress);
                    if (receptionistRoleName == null) throw new Exception("Unexpected missing role name: " + lastSenderAddress);

                    TestConductor.Exit(receptionistRoleName, 0).Wait();
                    _remainingServerRoleNames.Remove(receptionistRoleName);

                    Within(Remaining - TimeSpan.FromSeconds(3), () =>
                    {
                        AwaitAssert(() =>
                        {
                            c.Tell(new ClusterClient.Send("/user/service2", "hi again", localAffinity: true));
                            ExpectMsg("hi again-ack", TimeSpan.FromSeconds(1));
                        });
                    });
                    Sys.Stop(c);
                }, _client);
                EnterBarrier("verified-3");

                ReceiveWhile(TimeSpan.FromSeconds(2), msg =>
                {
                    if (msg.Equals("hi again")) return msg;
                    else throw new Exception("unexpected message: " + msg);
                });
                EnterBarrier("after-4");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void ClusterClient_should_reestablish_connection_to_receptionist_after_partition()
        {
            ClusterClient_should_reestablish_connection_to_another_receptionist_when_server_is_shutdown();

            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client3");
                    c.Tell(new ClusterClient.Send("/user/service2", "bonjour2", localAffinity: true));
                    ExpectMsg("bonjour2-ack");
                    var lastSenderAddress = LastSender.Path.Address;

                    var receptionistRoleName = RoleName(lastSenderAddress);
                    if (receptionistRoleName == null) throw new Exception("Unexpected missing role name: " + lastSenderAddress);

                    // shutdown all but the one that the client is connected to
                    foreach (var roleName in _remainingServerRoleNames.ToArray())
                        if (!roleName.Equals(receptionistRoleName)) TestConductor.Exit(roleName, 0).Wait();
                    _remainingServerRoleNames = new HashSet<RoleName>(new[] { receptionistRoleName });

                    // network partition between client and server
                    TestConductor.Blackhole(_client, receptionistRoleName, ThrottleTransportAdapter.Direction.Both).Wait();
                    c.Tell(new ClusterClient.Send("/user/service2", "ping", localAffinity: true));
                    // if we would use remote watch the failure detector would trigger and
                    // connection quarantined
                    ExpectNoMsg(TimeSpan.FromSeconds(5));

                    TestConductor.PassThrough(_client, receptionistRoleName, ThrottleTransportAdapter.Direction.Both).Wait();

                    var expectedAddress = Node(receptionistRoleName).Address;
                    AwaitAssert(() =>
                    {
                        c.Tell(new ClusterClient.Send("/user/service2", "bonjour3", localAffinity: true));
                        ExpectMsg("bonjour3-ack", TimeSpan.FromSeconds(1));
                        var lastSenderAddress2 = LastSender.Path.Address;
                        Assert.Equal(expectedAddress, lastSenderAddress2);
                    });
                    Sys.Stop(c);

                }, _client);
                EnterBarrier("after-5");
            });
        }
    }
}
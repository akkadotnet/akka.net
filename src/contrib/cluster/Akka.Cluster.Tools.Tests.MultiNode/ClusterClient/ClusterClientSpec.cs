//-----------------------------------------------------------------------
// <copyright file="ClusterClientSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Client;

public class ClusterClientSpecConfig : MultiNodeConfig
{
    public RoleName Client { get; }
    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }
    public RoleName Fourth { get; }

    public ClusterClientSpecConfig()
    {
        Client = Role("client");
        First = Role("first");
        Second = Role("second");
        Third = Role("third");
        Fourth = Role("fourth");

        CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.testconductor.query-timeout = 1m # we were having timeouts shutting down nodes with 5s default
                akka.actor.provider = cluster
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s
                akka.cluster.client.heartbeat-interval = 1s
                akka.cluster.client.acceptable-heartbeat-pause = 3s
                akka.cluster.client.refresh-contacts-interval = 1s
                # number-of-contacts must be >= 4 because we shutdown all but one in the end
                akka.cluster.client.receptionist.number-of-contacts = 4
                akka.cluster.client.receptionist.heartbeat-interval = 10s
                akka.cluster.client.receptionist.acceptable-heartbeat-pause = 10s
                akka.cluster.client.receptionist.failure-detection-interval = 1s
                akka.test.filter-leeway = 10s
            ")
            .WithFallback(ClusterClientReceptionist.DefaultConfig())
            .WithFallback(DistributedPubSub.DefaultConfig());

        TestTransport = true;
    }

    #region Helpers

    public class Reply
    {
        public Reply(object msg, Address node)
        {
            Msg = msg;
            Node = node;
        }

        public object Msg { get; }
        public Address Node { get; }
    }

    public class TestService : ReceiveActor
    {
        public TestService(IActorRef testActorRef)
        {
            Receive<string>(cmd => cmd.Equals("shutdown"), _ =>
            {
                Context.System.Terminate();
            });

            ReceiveAny(msg =>
            {
                testActorRef.Forward(msg);
                Sender.Tell(new Reply(msg.ToString() + "-ack", Cluster.Get(Context.System).SelfAddress));
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

    public class TestClientListener : ReceiveActor
    {
        #region TestClientListener messages
        public sealed class GetLatestContactPoints
        {
            public static readonly GetLatestContactPoints Instance = new();
            private GetLatestContactPoints() { }
        }

        public sealed class LatestContactPoints : INoSerializationVerificationNeeded
        {
            public LatestContactPoints(IImmutableSet<ActorPath> contactPoints)
            {
                ContactPoints = contactPoints;
            }

            public IImmutableSet<ActorPath> ContactPoints { get; }
        }

        #endregion

        private readonly IActorRef _targetClient;
        private IImmutableSet<ActorPath> _contactPoints;

        public TestClientListener(IActorRef targetClient)
        {
            _targetClient = targetClient;
            _contactPoints = ImmutableHashSet<ActorPath>.Empty;

            Receive<GetLatestContactPoints>(_ =>
            {
                Sender.Tell(new LatestContactPoints(_contactPoints));
            });

            Receive<ContactPoints>(cps =>
            {
                // Now do something with the up-to-date "cps"
                _contactPoints = cps.ContactPointsList;
            });

            Receive<ContactPointAdded>(cp =>
            {
                // Now do something with an up-to-date "contactPoints + cp"
                _contactPoints = _contactPoints.Add(cp.ContactPoint);
            });

            Receive<ContactPointRemoved>(cp =>
            {
                // Now do something with an up-to-date "contactPoints - cp"
                _contactPoints = _contactPoints.Remove(cp.ContactPoint);
            });
        }

        protected override void PreStart()
        {
            _targetClient.Tell(SubscribeContactPoints.Instance);
        }
    }

    public class TestReceptionistListener : ReceiveActor
    {
        #region TestReceptionistListener messages
        public sealed class GetLatestClusterClients
        {
            public static readonly GetLatestClusterClients Instance = new();
            private GetLatestClusterClients() { }
        }

        public sealed class LatestClusterClients : INoSerializationVerificationNeeded
        {
            public LatestClusterClients(IImmutableSet<IActorRef> clusterClients)
            {
                ClusterClients = clusterClients;
            }

            public IImmutableSet<IActorRef> ClusterClients { get; }
        }
        #endregion

        private readonly IActorRef _targetReceptionist;
        private IImmutableSet<IActorRef> _clusterClients;

        public TestReceptionistListener(IActorRef targetReceptionist)
        {
            _targetReceptionist = targetReceptionist;
            _clusterClients = ImmutableHashSet<IActorRef>.Empty;

            Receive<GetLatestClusterClients>(_ =>
            {
                Sender.Tell(new LatestClusterClients(_clusterClients));
            });

            Receive<ClusterClients>(cs =>
            {
                // Now do something with the up-to-date "c"
                _clusterClients = cs.ClusterClientsList;
            });

            Receive<ClusterClientUp>(c =>
            {
                // Now do something with an up-to-date "clusterClients + c"
                _clusterClients = _clusterClients.Add(c.ClusterClient);
            });

            Receive<ClusterClientUnreachable>(c =>
            {
                // Now do something with an up-to-date "clusterClients - c"
                _clusterClients = _clusterClients.Remove(c.ClusterClient);
            });
        }

        protected override void PreStart()
        {
            _targetReceptionist.Tell(SubscribeClusterClients.Instance);
        }
    }

    #endregion
}

public class ClusterClientSpec : MultiNodeClusterSpec
{
    private readonly ClusterClientSpecConfig _config;

    public ClusterClientSpec() : this(new ClusterClientSpecConfig())
    {
    }

    protected ClusterClientSpec(ClusterClientSpecConfig config) : base(config, typeof(ClusterClientSpec))
    {
        _config = config;
        _remainingServerRoleNames = ImmutableHashSet.Create(_config.First, _config.Second, _config.Third, _config.Fourth);
    }

    protected override int InitialParticipantsValueFactory
    {
        get { return Roles.Count; }
    }

    private async Task Join(RoleName from, RoleName to)
    {
        RunOn(() =>
        {
            Cluster.Join(Node(to).Address);
            CreateReceptionist();
        }, from);
        await EnterBarrierAsync(from.Name + "-joined");
    }

    private void CreateReceptionist()
    {
        ClusterClientReceptionist.Get(Sys);
    }

    private async Task AwaitCount(int expected)
    {
        await AwaitAssertAsync(async () =>
        {
            DistributedPubSub.Get(Sys).Mediator.Tell(Count.Instance);
            (await ExpectMsgAsync<int>()).Should().Be(expected);
        });
    }

    private RoleName GetRoleName(Address address)
    {
        return _remainingServerRoleNames.FirstOrDefault(r => Node(r).Address.Equals(address));
    }

    private ImmutableHashSet<RoleName> _remainingServerRoleNames;

    private ImmutableHashSet<ActorPath> InitialContacts
    {
        get
        {
            return _remainingServerRoleNames.Remove(_config.First).Remove(_config.Fourth).Select(r => Node(r) / "system" / "receptionist").ToImmutableHashSet();
        }
    }

    [MultiNodeFact]
    public async Task ClusterClientSpecs()
    {
        await ClusterClient_must_startup_cluster();
        await ClusterClient_must_communicate_to_any_node_in_cluster();
        await ClusterClient_must_work_with_ask();
        await ClusterClient_must_demonstrate_usage();
        await ClusterClient_must_report_events();
        await ClusterClient_must_report_removal_of_a_receptionist();
        await ClusterClient_must_reestablish_connection_to_another_receptionist_when_server_is_shutdown();
        await ClusterClient_must_reestablish_connection_to_receptionist_after_partition();
        await ClusterClient_must_reestablish_connection_to_receptionist_after_server_restart();
    }

    public async Task ClusterClient_must_startup_cluster()
    {
        await WithinAsync(30.Seconds(), async () =>
        {
            await Join(_config.First, _config.First);
            await Join(_config.Second, _config.First);
            await Join(_config.Third, _config.First);
            await Join(_config.Fourth, _config.First);

            RunOn(() =>
            {
                var service = Sys.ActorOf(Props.Create(() => new ClusterClientSpecConfig.TestService(TestActor)), "testService");
                ClusterClientReceptionist.Get(Sys).RegisterService(service);
            }, _config.Fourth);

            await RunOnAsync(async () =>
            {
                await AwaitCount(1);
            }, _config.First, _config.Second, _config.Third, _config.Fourth);

            await EnterBarrierAsync("after-1");
        });
    }

    public async Task ClusterClient_must_communicate_to_any_node_in_cluster()
    {
        await WithinAsync(10.Seconds(), async () =>
        {
            await RunOnAsync(async () =>
            {
                var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client1");
                c.Tell(new ClusterClient.Send("/user/testService", "hello", localAffinity: true));
                (await ExpectMsgAsync<ClusterClientSpecConfig.Reply>()).Msg.Should().Be("hello-ack");
                Sys.Stop(c);
            }, _config.Client);

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("hello");
            }, _config.Fourth);

            await EnterBarrierAsync("after-2");
        });
    }

    public async Task ClusterClient_must_work_with_ask()
    {
        await WithinAsync(10.Seconds(), async () =>
        {
            await RunOnAsync(async () =>
            {
                var c = Sys.ActorOf(ClusterClient.Props(
                    ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "ask-client");
                var reply = await c.Ask<ClusterClientSpecConfig.Reply>(new ClusterClient.Send("/user/testService", "hello-request", localAffinity: true));
                reply.Msg.Should().Be("hello-request-ack");
                Sys.Stop(c);
            }, _config.Client);

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("hello-request");
            }, _config.Fourth);

            await EnterBarrierAsync("after-3");
        });
    }

    public async Task ClusterClient_must_demonstrate_usage()
    {
        var host1 = _config.First;
        var host2 = _config.Second;
        var host3 = _config.Third;

        await WithinAsync(15.Seconds(), async () =>
        {
            //#server
            RunOn(() =>
            {
                var serviceA = Sys.ActorOf(Props.Create<ClusterClientSpecConfig.Service>(), "serviceA");
                ClusterClientReceptionist.Get(Sys).RegisterService(serviceA);
            }, host1);

            RunOn(() =>
            {
                var serviceB = Sys.ActorOf(Props.Create<ClusterClientSpecConfig.Service>(), "serviceB");
                ClusterClientReceptionist.Get(Sys).RegisterService(serviceB);
            }, host2, host3);
            //#server

            await RunOnAsync(async () =>
            {
                await AwaitCount(4);
            }, host1, host2, host3, _config.Fourth);
            await EnterBarrierAsync("services-replicated");

            //#client
            RunOn(() =>
            {
                var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client");
                c.Tell(new ClusterClient.Send("/user/serviceA", "hello", localAffinity: true));
                c.Tell(new ClusterClient.SendToAll("/user/serviceB", "hi"));
            }, _config.Client);
            //#client

            await RunOnAsync(async () =>
            {
                // note that "hi" was sent to 2 "serviceB"
                var received = await ReceiveNAsync(3).ToListAsync();
                received.ToImmutableHashSet().Should().BeEquivalentTo(ImmutableHashSet.Create("hello", "hi"));
            }, _config.Client);

            // strange, barriers fail without this sleep
            await Task.Delay(1000);
            await EnterBarrierAsync("after-4");
        });
    }

    public async Task ClusterClient_must_report_events()
    {
        await WithinAsync(15.Seconds(), async () =>
        {
            await RunOnAsync(async () =>
            {
                var c = await Sys.ActorSelection("/user/client").ResolveOne(Dilated(1.Seconds()));
                var l = Sys.ActorOf(
                    Props.Create(() => new ClusterClientSpecConfig.TestClientListener(c)),
                    "reporter-client-listener");

                var expectedContacts = ImmutableHashSet.Create(_config.First, _config.Second, _config.Third, _config.Fourth)
                    .Select(_ => Node(_) / "system" / "receptionist");

                await WithinAsync(10.Seconds(), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        var probe = CreateTestProbe();
                        l.Tell(ClusterClientSpecConfig.TestClientListener.GetLatestContactPoints.Instance, probe.Ref);
                        (await probe.ExpectMsgAsync<ClusterClientSpecConfig.TestClientListener.LatestContactPoints>())
                            .ContactPoints.Should()
                            .BeEquivalentTo(expectedContacts);
                    });
                });
            }, _config.Client);


            await EnterBarrierAsync("reporter-client-listener-tested");

            await RunOnAsync(async () =>
            {
                // Only run this test on a node that knows about our client. It could be that no node knows
                // but there isn't a means of expressing that at least one of the nodes needs to pass the test.
                var r = ClusterClientReceptionist.Get(Sys).Underlying;
                r.Tell(GetClusterClients.Instance);
                var cps = await ExpectMsgAsync<ClusterClients>();
                if (cps.ClusterClientsList.Any(c => c.Path.Name.Equals("client")))
                {
                    Log.Info("Testing that the receptionist has just one client");
                    var l = Sys.ActorOf(
                        Props.Create(() => new ClusterClientSpecConfig.TestReceptionistListener(r)),
                        "reporter-receptionist-listener");

                    var c = await Sys
                        .ActorSelection(Node(_config.Client) / "user" / "client")
                        .ResolveOne(Dilated(2.Seconds()));

                    var expectedClients = ImmutableHashSet.Create(c);
                    await WithinAsync(10.Seconds(), async () =>
                    {
                        await AwaitAssertAsync(async () =>
                        {
                            var probe = CreateTestProbe();
                            l.Tell(ClusterClientSpecConfig.TestReceptionistListener.GetLatestClusterClients.Instance, probe.Ref);

                            // "ask-client" might still be around, filter
                            (await probe.ExpectMsgAsync<ClusterClientSpecConfig.TestReceptionistListener.LatestClusterClients>())
                                .ClusterClients.Should()
                                .Contain(expectedClients);
                        });
                    });

                }

            }, _config.First, _config.Second, _config.Third);

            await EnterBarrierAsync("after-5");
        });
    }

    public async Task ClusterClient_must_report_removal_of_a_receptionist()
    {
        await WithinAsync(TimeSpan.FromSeconds(30), async () =>
        {
            await RunOnAsync(async () =>
            {
                var unreachableContact = await NodeAsync(_config.Client) / "system" / "receptionist";
                var expectedRoles =
                    ImmutableHashSet.Create(_config.First, _config.Second, _config.Third, _config.Fourth);
                var expectedContacts = expectedRoles.Select(x => Node(x) / "system" / "receptionist").ToImmutableHashSet();

                // We need to slow down things otherwise our receptionists can sometimes tell us
                // that our unreachableContact is unreachable before we get a chance to
                // subscribe to events.
                foreach (var role in expectedRoles)
                {
                    await TestConductor.BlackholeAsync(_config.Client, role, ThrottleTransportAdapter.Direction.Both);
                }

                var c = Sys.ActorOf(
                    ClusterClient.Props(ClusterClientSettings.Create(Sys)
                        .WithInitialContacts(expectedContacts.Add(unreachableContact))), "client5");

                var probe = CreateTestProbe();
                c.Tell(SubscribeContactPoints.Instance, probe.Ref);

                foreach (var role in expectedRoles)
                {
                    await TestConductor.PassThroughAsync(_config.Client, role, ThrottleTransportAdapter.Direction.Both);
                }

                await probe.FishForMessageAsync(o => (o is ContactPointRemoved cp && cp.ContactPoint.Equals(unreachableContact)), TimeSpan.FromSeconds(10), "removal");
            }, _config.Client);

            await EnterBarrierAsync("after-7");
        }); 
    }

    public async Task ClusterClient_must_reestablish_connection_to_another_receptionist_when_server_is_shutdown()
    {
        await WithinAsync(30.Seconds(), async () =>
        {
            await RunOnAsync(async () =>
            {
                var service2 = Sys.ActorOf(Props.Create(() => new ClusterClientSpecConfig.TestService(TestActor)), "service2");
                ClusterClientReceptionist.Get(Sys).RegisterService(service2);
                await AwaitCount(8);
            }, _config.First, _config.Second, _config.Third, _config.Fourth);
            await EnterBarrierAsync("service2-replicated");

            await RunOnAsync(async () =>
            {
                var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client2");
                c.Tell(new ClusterClient.Send("/user/service2", "bonjour", localAffinity: true));
                var reply = await ExpectMsgAsync<ClusterClientSpecConfig.Reply>();
                reply.Msg.Should().Be("bonjour-ack");

                RoleName receptionistRoleName = GetRoleName(reply.Node);
                if (receptionistRoleName == null)
                {
                    throw new Exception("Unexpected missing role name: " + reply.Node);
                }

                await TestConductor.ExitAsync(receptionistRoleName, 0);
                _remainingServerRoleNames = _remainingServerRoleNames.Remove(receptionistRoleName);

                await WithinAsync(Remaining - 3.Seconds(), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        c.Tell(new ClusterClient.Send("/user/service2", "hi again", localAffinity: true));
                        (await ExpectMsgAsync<ClusterClientSpecConfig.Reply>(1.Seconds())).Msg.Should().Be("hi again-ack");
                    });
                });
                Sys.Stop(c);
            }, _config.Client);

            await EnterBarrierAsync("verified-3");
            ReceiveWhile(2.Seconds(), msg =>
            {
                if (msg.Equals("hi again")) return msg;
                else throw new Exception("Unexpected message: " + msg);
            });
            await EnterBarrierAsync("verified-4");

            await RunOnAsync(async () =>
            {
                // Locate the test listener from a previous test and see that it agrees
                // with what the client is telling it about what receptionists are alive
                var l = Sys.ActorSelection("/user/reporter-client-listener");
                var expectedContacts = _remainingServerRoleNames.Select(c => Node(c) / "system" / "receptionist");
                await WithinAsync(10.Seconds(), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        var probe = CreateTestProbe();
                        l.Tell(ClusterClientSpecConfig.TestClientListener.GetLatestContactPoints.Instance, probe.Ref);
                        (await probe.ExpectMsgAsync<ClusterClientSpecConfig.TestClientListener.LatestContactPoints>())
                            .ContactPoints.Should()
                            .BeEquivalentTo(expectedContacts);
                    });
                });
            }, _config.Client);

            await EnterBarrierAsync("after-6");
        });
    }

    public async Task ClusterClient_must_reestablish_connection_to_receptionist_after_partition()
    {
        await WithinAsync(30.Seconds(), async () =>
        {
            await RunOnAsync(async () =>
            {
                var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(InitialContacts)), "client3");
                c.Tell(new ClusterClient.Send("/user/service2", "bonjour2", localAffinity: true));
                var reply = await ExpectMsgAsync<ClusterClientSpecConfig.Reply>();
                reply.Msg.Should().Be("bonjour2-ack");

                RoleName receptionistRoleName = GetRoleName(reply.Node);
                if (receptionistRoleName == null)
                {
                    throw new Exception("Unexpected missing role name: " + reply.Node);
                }

                // shutdown all but the one that the client is connected to
                var exitTasks = _remainingServerRoleNames.Where(r => !r.Equals(receptionistRoleName)).Select(r => TestConductor.ExitAsync(r, 0));

                await Task.WhenAll(exitTasks.ToArray());
                _remainingServerRoleNames = ImmutableHashSet.Create(receptionistRoleName);

                // network partition between client and server
                await TestConductor.BlackholeAsync(_config.Client, receptionistRoleName, ThrottleTransportAdapter.Direction.Both);
                c.Tell(new ClusterClient.Send("/user/service2", "ping", localAffinity: true));
                // if we would use remote watch the failure detector would trigger and
                // connection quarantined
                await ExpectNoMsgAsync(5.Seconds());

                await TestConductor.PassThroughAsync(_config.Client, receptionistRoleName, ThrottleTransportAdapter.Direction.Both);

                var expectedAddress = GetAddress(receptionistRoleName);
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    c.Tell(new ClusterClient.Send("/user/service2", "bonjour3", localAffinity: true), probe.Ref);
                    var reply2 = await probe.ExpectMsgAsync<ClusterClientSpecConfig.Reply>(1.Seconds());
                    reply2.Msg.Should().Be("bonjour3-ack");
                    reply2.Node.Should().Be(expectedAddress);
                });
                Sys.Stop(c);
            }, _config.Client);

            await EnterBarrierAsync("after-8");
        });
    }

    public async Task ClusterClient_must_reestablish_connection_to_receptionist_after_server_restart()
    {
        await WithinAsync(30.Seconds(), async () =>
        {
            await RunOnAsync(async () =>
            {
                _remainingServerRoleNames.Count.Should().Be(1);
                var remainingContacts = _remainingServerRoleNames.Select(r => Node(r) / "system" / "receptionist").ToImmutableHashSet();
                var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithInitialContacts(remainingContacts)), "client4");

                c.Tell(new ClusterClient.Send("/user/service2", "bonjour4", localAffinity: true));
                var reply = ExpectMsg<ClusterClientSpecConfig.Reply>(10.Seconds());
                reply.Msg.Should().Be("bonjour4-ack");
                reply.Node.Should().Be(remainingContacts.First().Address);

                var logSource = $"{Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress}/user/client4";

                await EventFilter.Info(start: "Connected to", source:logSource).ExpectOneAsync(async () =>
                {
                    await EventFilter.Info(start: "Lost contact", source:logSource).ExpectOneAsync(async () =>
                    {
                        // shutdown server
                        await TestConductor.ShutdownAsync(_remainingServerRoleNames.First());
                    });
                });

                c.Tell(new ClusterClient.Send("/user/service2", "shutdown", localAffinity: true));
                await Task.Delay(2000); // to ensure that it is sent out before shutting down system
            }, _config.Client);

            await RunOnAsync(async () =>
            {
                await Sys.WhenTerminated.WaitAsync(20.Seconds());
                // start new system on same port
                var port = Cluster.Get(Sys).SelfAddress.Port;
                var sys2 = ActorSystem.Create(
                    Sys.Name,
                    ConfigurationFactory.ParseString($"akka.remote.dot-netty.tcp.port={port}").WithFallback(Sys.Settings.Config));
                Cluster.Get(sys2).Join(Cluster.Get(sys2).SelfAddress);
                var service2 = sys2.ActorOf(Props.Create(() => new ClusterClientSpecConfig.TestService(TestActor)), "service2");
                ClusterClientReceptionist.Get(sys2).RegisterService(service2);
                await sys2.WhenTerminated.WaitAsync(20.Seconds());
            }, _remainingServerRoleNames.ToArray());
        });
    }
}
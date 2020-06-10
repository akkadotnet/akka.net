//-----------------------------------------------------------------------
// <copyright file="ClusterClientStartSpecConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Client
{
    
    public class ClusterClientStartSpecConfig : MultiNodeConfig
    {
        public RoleName Client { get; }
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterClientStartSpecConfig()
        {
            Client = Role("client");
            First = Role("first");
            Second = Role("second");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.client {
                  heartbeat-interval = 1s
                  acceptable-heartbeat-pause = 1s
                  reconnect-timeout = 60s
                  receptionist.number-of-contacts = 1
                }
                akka.test.filter-leeway = 10s
            ")
                .WithFallback(ClusterClientReceptionist.DefaultConfig())
                .WithFallback(DistributedPubSub.DefaultConfig());
        }

        public class Service : ReceiveActor
        {
            public Service()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        public class ClusterClientStartSpec : MultiNodeClusterSpec
        {
            private readonly ClusterClientStartSpecConfig _config;

            public ClusterClientStartSpec() : this(new ClusterClientStartSpecConfig())
            {
            }

            protected ClusterClientStartSpec(ClusterClientStartSpecConfig config) : base(config, typeof(ClusterClientStartSpec))
            {
                _config = config;
            }

            protected override int InitialParticipantsValueFactory => 3;

            private ImmutableHashSet<ActorPath> InitialContacts
            {
                get
                {
                    //return new List<ActorPath>().ToImmutableHashSet();
                    return ImmutableHashSet
                        .Create(_config.First, _config.Second)
                        .Select(r => Node(r) / "system" / "receptionist")
                        .ToImmutableHashSet();
                }
            }

            [MultiNodeFact(Skip = "Disable due to known issues with this spec which are currently under investigation by ArjenSmitss")]
            public void ClusterClientStartSpecs()
            {
                Start_Cluster();
                ClusterClient_can_start_with_zero_buffer();
            }

            public void Start_Cluster()
            {
                Within(30.Seconds(), () =>
                {
                    AwaitClusterUp(_config.First, _config.Second);
                    
                    //start our test service
                    RunOn(() =>
                    {
                        var service = Sys.ActorOf(Props.Create(() => new ClusterClientStopSpecConfig.Service()), "testService");

                        //here we explicitly do _not_ register the service with the cluster receptionist to force the clusterclient in buffer mode
                        //ClusterClientReceptionist.Get(Sys).RegisterService(service);
                    }, _config.First, _config.Second);

                    EnterBarrier("receptionist-started");
                });
            }

            public void ClusterClient_can_start_with_zero_buffer()
            {
                Within(30.Seconds(), () =>
                {
                    //start the cluster client 
                    RunOn(() =>
                    {
                        var c = Sys.ActorOf(ClusterClient.Props(ClusterClientSettings.Create(Sys).WithBufferSize(0).WithInitialContacts(InitialContacts)), "client1");
                        //check for the debug log message that the cluster client will output in case of an 0 buffersize
                        EventFilter.Debug(start: "Receptionist not available and buffering is disabled, dropping message").ExpectOne(
                            () =>
                            {
                                c.Tell(new ClusterClient.Send("/user/testService", "hello"));
                            });

                        //ExpectMsg<string>(3.Seconds()).Should().Be("hello");
                        
                       
                        ExpectTerminated(c, 10.Seconds());
                    }, _config.Client);

                    EnterBarrier("end-of-test");
                });
            }
        }
    }
}

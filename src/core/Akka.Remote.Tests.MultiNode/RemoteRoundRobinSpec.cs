//-----------------------------------------------------------------------
// <copyright file="RemoteRoundRobinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Remote.TestKit;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests.MultiNode
{
    public class RoundRobinMultiNodeConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }
        public RoleName Third { get; private set; }
        public RoleName Fourth { get; private set; }

        public RoundRobinMultiNodeConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = DebugConfig(false);

            DeployOnAll(@"
      /service-hello {
        router = round-robin-pool
        nr-of-instances = 3
        target.nodes = [""@first@"", ""@second@"", ""@third@""]
      }
      /service-hello2 {
        router = round-robin-pool
        target.nodes = [""@first@"", ""@second@"", ""@third@""]
      }
      /service-hello3 {
        router = round-robin-group
        routees.paths = [
          ""@first@/user/target-first"",
          ""@second@/user/target-second"",
          ""@third@/user/target-third""]
      }
           ");
        }
    }

    public class RemoteRoundRobinMultiNode1 : RemoteRoundRobinSpec
    {
    }

    public class RemoteRoundRobinMultiNode2 : RemoteRoundRobinSpec
    {
    }

    public class RemoteRoundRobinMultiNode3 : RemoteRoundRobinSpec
    {
    }

    public class RemoteRoundRobinMultiNode4 : RemoteRoundRobinSpec
    {
    }

    public abstract class RemoteRoundRobinSpec : MultiNodeSpec
    {
        private readonly RoundRobinMultiNodeConfig _config;

        protected RemoteRoundRobinSpec() : this(new RoundRobinMultiNodeConfig())
        {
        }

        protected RemoteRoundRobinSpec(RoundRobinMultiNodeConfig config) : base(config)
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message.Equals("hit"))
                {
                    Sender.Tell(Self);
                }
            }
        }

        public class TestResizer : Resizer
        {
            public override bool IsTimeForResize(long messageCounter)
            {
                return messageCounter <= 10;
            }

            public override int Resize(IEnumerable<Routee> currentRoutees)
            {
                return 1;
            }
        }

        [MultiNodeFact]
        public void RemoteRoundRobinSpecs()
        {
            ARemoteRoundRobinMustBeLocallyInstantiatedOnARemoteNodeAndBeAbleToCommunicateThroughItsRemoteActorRef();

            /*
            Test is commented out until it is no longer flaky (issue1311 https://github.com/akkadotnet/akka.net/issues/1311).
            */
            //ARemoteRoundRobinPoolWithResizerMustBeLocallyInstantiatedOnARemoteNodeAfterSeveralResizeRounds();

            ARemoteRoundRobinGroupMustSendMessagesWithActorSelectionToRemotePaths();
        }

        public void
            ARemoteRoundRobinMustBeLocallyInstantiatedOnARemoteNodeAndBeAbleToCommunicateThroughItsRemoteActorRef()
        {
            RunOn(() => { EnterBarrier("start", "broadcast-end", "end"); },
                _config.First, _config.Second, _config.Third);

            var runOnFourth = new Action(() =>
            {
                EnterBarrier("start");
                var actor = Sys.ActorOf(new RoundRobinPool(nrOfInstances: 0)
                    .Props(Props.Create<SomeActor>()), "service-hello");

                Assert.IsType<RoutedActorRef>(actor);

                var connectionCount = 3;
                var iterationCount = 10;

                for (var i = 0; i < iterationCount; i++)
                    for (var k = 0; k < connectionCount; k++)
                        actor.Tell("hit");

                var replies = ReceiveWhile(TimeSpan.FromSeconds(5), x =>
                {
                    if (x is IActorRef) return x.AsInstanceOf<IActorRef>().Path.Address;
                    return null;
                }, connectionCount*iterationCount)
                    .Aggregate(ImmutableDictionary<Address, int>.Empty
                        .Add(Node(_config.First).Address, 0)
                        .Add(Node(_config.Second).Address, 0)
                        .Add(Node(_config.Third).Address, 0),
                        (map, address) =>
                        {
                            var previous = map[address];
                            return map.Remove(address).Add(address, previous + 1);
                        });


                EnterBarrier("broadcast-end");
                actor.Tell(new Broadcast(PoisonPill.Instance));

                EnterBarrier("end");
                replies.Values.ForEach(x => Assert.Equal(x, iterationCount));
                Assert.False(replies.ContainsKey(Node(_config.Fourth).Address));

                Sys.Stop(actor);
            });

            RunOn(runOnFourth, _config.Fourth);
            EnterBarrier("done");
        }

        public void ARemoteRoundRobinPoolWithResizerMustBeLocallyInstantiatedOnARemoteNodeAfterSeveralResizeRounds()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                RunOn(() => { EnterBarrier("start", "broadcast-end", "end"); },
                    _config.First, _config.Second, _config.Third);

                var runOnFourth = new Action(() =>
                {
                    EnterBarrier("start");
                    var actor = Sys.ActorOf(new RoundRobinPool(
                        nrOfInstances: 1,
                        resizer: new TestResizer()
                        ).Props(Props.Create<SomeActor>()), "service-hello2");

                    Assert.IsType<RoutedActorRef>(actor);

                    actor.Tell(RouterMessage.GetRoutees);
                    ExpectMsg<Routees>().Members.Count().ShouldBe(2);

                    var repliesFrom = Enumerable.Range(3, 7).Select(n =>
                    {
                        //each message triggers a resize, incrementing number of routees with 1
                        actor.Tell("hit");
                        var routees = actor.AskAndWait<Routees>(RouterMessage.GetRoutees, TimeSpan.FromSeconds(5));
                        routees.Members.Count().ShouldBe(n);
                        return ExpectMsg<IActorRef>();
                    }).ToImmutableHashSet();

                    EnterBarrier("broadcast-end");
                    actor.Tell(new Broadcast(PoisonPill.Instance));

                    EnterBarrier("end");
                    Assert.Equal(repliesFrom.Count, 7);
                    var repliesFromAddresses = repliesFrom.Select(x => x.Path.Address).Distinct();
                    var expectedAddresses = new List<ActorPath>
                    {
                        Node(_config.First),
                        Node(_config.Second),
                        Node(_config.Third)
                    }
                        .Select(x => x.Address);

                    // check if they have same elements (ignoring order)
                    Assert.All(repliesFromAddresses, x => expectedAddresses.Contains(x));
                    Assert.True(repliesFromAddresses.Count() == expectedAddresses.Count());

                    Sys.Stop(actor);
                });

                RunOn(runOnFourth, _config.Fourth);
                EnterBarrier("done");
            });
        }

        public void ARemoteRoundRobinGroupMustSendMessagesWithActorSelectionToRemotePaths()
        {
            RunOn(() =>
            {
                Sys.ActorOf<SomeActor>(name: "target-" + Myself.Name);
                EnterBarrier("start", "end");
            }, _config.First, _config.Second, _config.Third);

            var runOnFourth = new Action(() =>
            {
                EnterBarrier("start");
                var actor = Sys.ActorOf(Props.Create<BlackHoleActor>().WithRouter(FromConfig.Instance), "service-hello3");

                Assert.IsType<RoutedActorRef>(actor);

                var connectionCount = 3;
                var iterationCount = 10;

                for (var i = 0; i < iterationCount; i++)
                    for (var k = 0; k < connectionCount; k++)
                        actor.Tell("hit");

                var replies = ReceiveWhile(TimeSpan.FromSeconds(5), x =>
                {
                    if (x is IActorRef) return x.AsInstanceOf<IActorRef>().Path.Address;
                    return null;
                }, connectionCount*iterationCount)
                    .Aggregate(ImmutableDictionary<Address, int>.Empty
                        .Add(Node(_config.First).Address, 0)
                        .Add(Node(_config.Second).Address, 0)
                        .Add(Node(_config.Third).Address, 0),
                        (map, address) =>
                        {
                            var previous = map[address];
                            return map.Remove(address).Add(address, previous + 1);
                        });

                EnterBarrier("end");
                replies.Values.ForEach(x => Assert.Equal(x, iterationCount));
                Assert.False(replies.ContainsKey(Node(_config.Fourth).Address));
            });

            RunOn(runOnFourth, _config.Fourth);
            EnterBarrier("done");
        }
    }
}
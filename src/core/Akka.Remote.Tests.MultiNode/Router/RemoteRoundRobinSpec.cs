//-----------------------------------------------------------------------
// <copyright file="RemoteRoundRobinSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.MultiNode.Router
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

            CommonConfig = DebugConfig(true);

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

    public class RemoteRoundRobinSpec : MultiNodeSpec
    {
        private readonly RoundRobinMultiNodeConfig _config;

        public RemoteRoundRobinSpec() : this(new RoundRobinMultiNodeConfig())
        {
        }

        protected RemoteRoundRobinSpec(RoundRobinMultiNodeConfig config) : base(config, typeof(RemoteRoundRobinSpec))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        private class SomeActor : UntypedActor
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
            A_remote_round_robin_pool_must_be_locally_instantiated_on_a_remote_node_and_be_able_to_communicate_through_its_remote_actor_ref();
            A_remote_round_robin_pool_with_resizer_must_be_locally_instantiated_on_a_remote_node_after_several_resize_rounds();
            A_remote_round_robin_group_must_send_messages_with_actor_selection_to_remote_paths();
        }

        public void A_remote_round_robin_pool_must_be_locally_instantiated_on_a_remote_node_and_be_able_to_communicate_through_its_remote_actor_ref()
        {
            RunOn(() =>
            {
                EnterBarrier("start", "broadcast-end", "end");
            }, _config.First, _config.Second, _config.Third);

            RunOn(() =>
            {
                EnterBarrier("start");
                var actor = Sys.ActorOf(new RoundRobinPool(nrOfInstances: 0)
                    .Props(Props.Create<SomeActor>()), "service-hello");
                actor.Should().BeOfType<RoutedActorRef>();

                var connectionCount = 3;
                var iterationCount = 10;

                for (var i = 0; i < iterationCount; i++)
                    for (var k = 0; k < connectionCount; k++)
                        actor.Tell("hit");

                var replies = ReceiveWhile(TimeSpan.FromSeconds(5), x =>
                {
                    if (x is IActorRef) return x.AsInstanceOf<IActorRef>().Path.Address;
                    return null;
                }, connectionCount * iterationCount)
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
                replies.Values.ForEach(x => x.Should().Be(iterationCount));
                replies.ContainsKey(Node(_config.Fourth).Address).Should().BeFalse();

                // shut down the actor before we let the other node(s) shut down so we don't try to send
                // "Terminate" to a shut down node
                Sys.Stop(actor);
            }, _config.Fourth);
            EnterBarrier("done");
        }

        public void A_remote_round_robin_pool_with_resizer_must_be_locally_instantiated_on_a_remote_node_after_several_resize_rounds()
        {
            Within(TimeSpan.FromSeconds(5), () =>
            {
                RunOn(() =>
                {
                    EnterBarrier("start", "broadcast-end", "end");
                }, _config.First, _config.Second, _config.Third);

                RunOn(() =>
                {
                    EnterBarrier("start");
                    var actor = Sys.ActorOf(new RoundRobinPool(
                        nrOfInstances: 1,
                        resizer: new TestResizer()
                        ).Props(Props.Create<SomeActor>()), "service-hello2");
                    actor.Should().BeOfType<RoutedActorRef>();

                    actor.Tell(RouterMessage.GetRoutees);
                    // initial nrOfInstances 1 + initial resize => 2
                    ExpectMsg<Routees>().Members.Count().Should().Be(2);

                    ImmutableHashSet<IActorRef> repliesFrom = ImmutableHashSet.Create<IActorRef>();

                    for (int i = 3; i <= 9; i++)
                    {
                        //each message triggers a resize, incrementing number of routees with 1
                        actor.Tell("hit");
                        var routees = actor.AskAndWait<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
                        routees.Members.Count().Should().Be(i);
                        repliesFrom = repliesFrom.Add(ExpectMsg<IActorRef>());
                    }

                    EnterBarrier("broadcast-end");
                    actor.Tell(new Broadcast(PoisonPill.Instance));

                    EnterBarrier("end");
                    repliesFrom.Count.Should().Be(7);
                    var repliesFromAddresses = repliesFrom.Select(x => x.Path.Address).ToImmutableHashSet();
                    repliesFromAddresses.Should().BeEquivalentTo(new List<Address>
                    {
                        Node(_config.First).Address,
                        Node(_config.Second).Address,
                        Node(_config.Third).Address
                    });

                    Sys.Stop(actor);
                }, _config.Fourth);
                EnterBarrier("done");
            });
        }

        public void A_remote_round_robin_group_must_send_messages_with_actor_selection_to_remote_paths()
        {
            RunOn(() =>
            {
                Sys.ActorOf<SomeActor>(name: "target-" + Myself.Name);
                EnterBarrier("start", "end");
            }, _config.First, _config.Second, _config.Third);

            RunOn(() =>
            {
                EnterBarrier("start");
                var actor = Sys.ActorOf(FromConfig.Instance.Props(), "service-hello3");
                actor.Should().BeOfType<RoutedActorRef>();

                var connectionCount = 3;
                var iterationCount = 10;

                for (var i = 0; i < iterationCount; i++)
                    for (var k = 0; k < connectionCount; k++)
                        actor.Tell("hit");

                var replies = ReceiveWhile(TimeSpan.FromSeconds(5), x =>
                {
                    if (x is IActorRef) return x.AsInstanceOf<IActorRef>().Path.Address;
                    return null;
                }, connectionCount * iterationCount)
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
                replies.Values.ForEach(x => x.Should().Be(iterationCount));
                replies.ContainsKey(Node(_config.Fourth).Address).Should().BeFalse();
            }, _config.Fourth);
            EnterBarrier("done");
        }
    }
}

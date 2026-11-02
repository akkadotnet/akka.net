//-----------------------------------------------------------------------
// <copyright file="ConsistentHashingRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Routing
{
    public class ConsistentHashingRouterSpec : AkkaSpec
    {
        #region Actors & Message Classes

        public class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message is ConsistentHashableEnvelope)
                {
                    Sender.Tell(string.Format("Unexpected envelope: {0}", message));
                }
                else
                {
                    Sender.Tell(Self);
                }
            }
        }

        public sealed class Msg : IConsistentHashable
        {
            public Msg(object consistentHashKey, string data)
            {
                ConsistentHashKey = consistentHashKey;
                Data = data;
            }

            public string Data { get; private set; }

            public object Key { get { return ConsistentHashKey; } }

            public object ConsistentHashKey { get; private set; }
        }

        public sealed class MsgKey
        {
            public MsgKey(string name)
            {
                Name = name;
            }

            public string Name { get; private set; }
        }

        public sealed class Msg2
        {
            public Msg2(object key, string data)
            {
                Data = data;
                Key = key;
            }

            public string Data { get; private set; }

            public object Key { get; private set; }
        }

        /// <summary>
        /// A minimal <see cref="Routee"/> whose <see cref="ToString"/> we control, so a test can force
        /// two routees whose virtual nodes collide in the 32-bit ring (see #8031).
        /// </summary>
        public sealed class NamedRoutee : Routee
        {
            private readonly string _name;

            public NamedRoutee(string name) => _name = name;

            public override string ToString() => _name;
        }

        #endregion

        private readonly IActorRef _router1;
        private readonly IActorRef _router3;
        private IActorRef a, b, c;

        public ConsistentHashingRouterSpec()
            : base(@"
            akka.actor.deployment {
              /router1 {
                router = consistent-hashing-pool
                nr-of-instances = 3
                virtual-nodes-factor = 17
              }
              /router2 {
                router = consistent-hashing-pool
                nr-of-instances = 5
              }
              /router3 {
                router = consistent-hashing-group
                virtual-nodes-factor = 17
                routees.paths = [""/user/A"",""/user/B"",""/user/C""]
              }
              /router4 {
                router = consistent-hashing-group
                routees.paths = [""/user/A"",""/user/B"",""/user/C"", ]
              }
            }
        ")
        {
            _router1 = Sys.ActorOf(Props.Create<Echo>().WithRouter(FromConfig.Instance), "router1");
            a = Sys.ActorOf(Props.Create<Echo>(), "A");
            b = Sys.ActorOf(Props.Create<Echo>(), "B");
            c = Sys.ActorOf(Props.Create<Echo>(), "C");
            _router3 = Sys.ActorOf(Props.Create<Echo>().WithRouter(FromConfig.Instance), "router3");
        }

        [Fact]
        public async Task Consistent_hashing_pool_router_must_create_routees_from_configuration()
        {
            var currentRoutees = await _router1.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            currentRoutees.Members.Count().Should().Be(3);
        }

        [Fact]
        public async Task Consistent_hashing_pool_router_must_select_destination_based_on_consistent_hash_key_of_message()
        {
            _router1.Tell(new Msg("a", "A"));
            var destinationA = await ExpectMsgAsync<IActorRef>();
            _router1.Tell(new ConsistentHashableEnvelope("AA", "a"));
            await ExpectMsgAsync(destinationA);

            _router1.Tell(new Msg(17, "A"));
            var destinationB = await ExpectMsgAsync<IActorRef>();
            _router1.Tell(new ConsistentHashableEnvelope("BB", 17));
            await ExpectMsgAsync(destinationB);

            _router1.Tell(new Msg(new MsgKey("c"), "C"));
            var destinationC = await ExpectMsgAsync<IActorRef>();
            _router1.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            await ExpectMsgAsync(destinationC);
        }

        [Fact]
        public async Task Consistent_hashing_pool_router_must_select_destination_with_defined_hash_mapping()
        {
            ConsistentHashMapping hashMapping = msg =>
            {
                if (msg is Msg2)
                {
                    var m2 = msg as Msg2;
                    return m2.Key;
                }

                return null;
            };
            var router2 = Sys.ActorOf(new ConsistentHashingPool(1, hashMapping).Props(Props.Create<Echo>()), "router2");

            router2.Tell(new Msg2("a", "A"));
            var destinationA = await ExpectMsgAsync<IActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("AA", "a"));
            await ExpectMsgAsync(destinationA);

            router2.Tell(new Msg2(17, "A"));
            var destinationB = await ExpectMsgAsync<IActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("BB", 17));
            await ExpectMsgAsync(destinationB);

            router2.Tell(new Msg2(new MsgKey("c"), "C"));
            var destinationC = await ExpectMsgAsync<IActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            await ExpectMsgAsync(destinationC);
        }

        [Fact]
        public async Task Consistent_hashing_group_router_must_create_routees_from_configuration()
        {
            var currentRoutees = await _router3.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            currentRoutees.Members.Count().ShouldBe(3);
        }

        [Fact]
        public async Task Consistent_hashing_group_router_must_select_destination_based_on_consistent_hash_key_of_message()
        {
            _router3.Tell(new Msg("a", "A"));
            var destinationA = await ExpectMsgAsync<IActorRef>();
            _router3.Tell(new ConsistentHashableEnvelope("AA", "a"));
            await ExpectMsgAsync(destinationA);

            _router3.Tell(new Msg(17, "A"));
            var destinationB = await ExpectMsgAsync<IActorRef>();
            _router3.Tell(new ConsistentHashableEnvelope("BB", 17));
            await ExpectMsgAsync(destinationB);

            _router3.Tell(new Msg(new MsgKey("c"), "C"));
            var destinationC = await ExpectMsgAsync<IActorRef>();
            _router3.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            await ExpectMsgAsync(destinationC);
        }

        [Fact]
        public async Task Consistent_hashing_group_router_must_select_destination_with_defined_hash_mapping()
        {
            ConsistentHashMapping hashMapping = msg =>
            {
                if (msg is Msg2)
                {
                    var m2 = msg as Msg2;
                    return m2.Key;
                }

                return null;
            };

            var paths = new List<string> { c.Path.ToString() };
            var router4 = Sys.ActorOf(new ConsistentHashingGroup(paths, hashMapping).Props(), "router4");

            router4.Tell(new Msg2("a", "A"));
            var destinationA = await ExpectMsgAsync<IActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("AA", "a"));
            await ExpectMsgAsync(destinationA);

            router4.Tell(new Msg2(17, "A"));
            var destinationB = await ExpectMsgAsync<IActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("BB", 17));
            await ExpectMsgAsync(destinationB);

            router4.Tell(new Msg2(new MsgKey("c"), "C"));
            var destinationC = await ExpectMsgAsync<IActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            await ExpectMsgAsync(destinationC);
        }

        [Fact]
        public void Consistent_hashing_routing_logic_must_not_wedge_when_routees_collide_in_the_ring()
        {
            // "2842" and "7681" have colliding virtual nodes at virtual-nodes-factor 10. Before #8031
            // this threw inside ConsistentHash.Create, was swallowed by Select, and every message
            // routed to NoRoutee cluster-wide until a manual restart. The ring now probes past the
            // collision, so Select must resolve to one of the real routees.
            var routeeA = new NamedRoutee("2842");
            var routeeB = new NamedRoutee("7681");
            var routees = new Routee[] { routeeA, routeeB };
            var logic = new ConsistentHashingRoutingLogic(Sys, 10, ConsistentHashingRouter.EmptyConsistentHashMapping);

            var selected = logic.Select(new ConsistentHashableEnvelope("payload", "some-key"), routees);

            selected.Should().NotBeSameAs(Routee.NoRoutee, "a 32-bit collision must no longer wedge the router");
            selected.Should().Match<Routee>(r => ReferenceEquals(r, routeeA) || ReferenceEquals(r, routeeB));
        }
    }
}

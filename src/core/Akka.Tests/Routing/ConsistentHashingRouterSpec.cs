//-----------------------------------------------------------------------
// <copyright file="ConsistentHashingRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        public void Consistent_hashing_pool_router_must_select_destination_based_on_consistent_hash_key_of_message()
        {
            _router1.Tell(new Msg("a", "A"));
            var destinationA = ExpectMsg<IActorRef>();
            _router1.Tell(new ConsistentHashableEnvelope("AA", "a"));
            ExpectMsg(destinationA);

            _router1.Tell(new Msg(17, "A"));
            var destinationB = ExpectMsg<IActorRef>();
            _router1.Tell(new ConsistentHashableEnvelope("BB", 17));
            ExpectMsg(destinationB);

            _router1.Tell(new Msg(new MsgKey("c"), "C"));
            var destinationC = ExpectMsg<IActorRef>();
            _router1.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            ExpectMsg(destinationC);
        }

        [Fact]
        public void Consistent_hashing_pool_router_must_select_destination_with_defined_hash_mapping()
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
            var destinationA = ExpectMsg<IActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("AA", "a"));
            ExpectMsg(destinationA);

            router2.Tell(new Msg2(17, "A"));
            var destinationB = ExpectMsg<IActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("BB", 17));
            ExpectMsg(destinationB);

            router2.Tell(new Msg2(new MsgKey("c"), "C"));
            var destinationC = ExpectMsg<IActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            ExpectMsg(destinationC);
        }

        [Fact]
        public async Task Consistent_hashing_group_router_must_create_routees_from_configuration()
        {
            var currentRoutees = await _router3.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            currentRoutees.Members.Count().ShouldBe(3);
        }

        [Fact]
        public void Consistent_hashing_group_router_must_select_destination_based_on_consistent_hash_key_of_message()
        {
            _router3.Tell(new Msg("a", "A"));
            var destinationA = ExpectMsg<IActorRef>();
            _router3.Tell(new ConsistentHashableEnvelope("AA", "a"));
            ExpectMsg(destinationA);

            _router3.Tell(new Msg(17, "A"));
            var destinationB = ExpectMsg<IActorRef>();
            _router3.Tell(new ConsistentHashableEnvelope("BB", 17));
            ExpectMsg(destinationB);

            _router3.Tell(new Msg(new MsgKey("c"), "C"));
            var destinationC = ExpectMsg<IActorRef>();
            _router3.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            ExpectMsg(destinationC);
        }

        [Fact]
        public void Consistent_hashing_group_router_must_select_destination_with_defined_hash_mapping()
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
            var destinationA = ExpectMsg<IActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("AA", "a"));
            ExpectMsg(destinationA);

            router4.Tell(new Msg2(17, "A"));
            var destinationB = ExpectMsg<IActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("BB", 17));
            ExpectMsg(destinationB);

            router4.Tell(new Msg2(new MsgKey("c"), "C"));
            var destinationC = ExpectMsg<IActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            ExpectMsg(destinationC);
        }
    }
}

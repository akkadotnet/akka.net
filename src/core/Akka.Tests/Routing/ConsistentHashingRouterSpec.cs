using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

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

        private ActorRef router1;
        private ActorRef router3;
        private ActorRef a, b, c;

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
                routees.paths = [""user/A"",""user/B"",""user/C""]
              }
              /router4 {
                router = consistent-hashing-group
                routees.paths = [""user/A"",""user/B"",""user/C"", ]
              }
        ")
        {
            router1 = Sys.ActorOf(Props.Create<Echo>().WithRouter(FromConfig.Instance), "router1");
            a = Sys.ActorOf(Props.Create<Echo>(), "A");
            b = Sys.ActorOf(Props.Create<Echo>(), "B");
            c = Sys.ActorOf(Props.Create<Echo>(), "C");
            router3 = Sys.ActorOf(Props.Create<Echo>().WithRouter(FromConfig.Instance), "router3");
        }

        [Fact]
        public async Task ConsistentHashingRouterMustCreateRouteesFromConfiguration()
        {
            var currentRoutees = await router1.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            currentRoutees.Members.Count().ShouldBe(3);
        }

        [Fact]
        public void ConsistentHashingRouterMustSelectDestinationBasedOnConsistentHashKeyOfMessage()
        {
            router1.Tell(new Msg("a", "A"));
            var destinationA = ExpectMsg<ActorRef>();
            router1.Tell(new ConsistentHashableEnvelope("AA", "a"));
            ExpectMsg(destinationA);

            router1.Tell(new Msg(17, "A"));
            var destinationB = ExpectMsg<ActorRef>();
            router1.Tell(new ConsistentHashableEnvelope("BB", 17));
            ExpectMsg(destinationB);

            router1.Tell(new Msg(new MsgKey("c"), "C"));
            var destinationC = ExpectMsg<ActorRef>();
            router1.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            ExpectMsg(destinationC);
        }

        [Fact]
        public void ConsistentHashingRouterMustSelectDestinationWithDefinedHashMapping()
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
            var router2 =
                Sys.ActorOf(new ConsistentHashingPool(1, null, null, null, hashMapping: hashMapping).Props(Props.Create<Echo>()), "router2");

            router2.Tell(new Msg2("a", "A"));
            var destinationA = ExpectMsg<ActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("AA", "a"));
            ExpectMsg(destinationA);

            router2.Tell(new Msg2(17, "A"));
            var destinationB = ExpectMsg<ActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("BB", 17));
            ExpectMsg(destinationB);

            router2.Tell(new Msg2(new MsgKey("c"), "C"));
            var destinationC = ExpectMsg<ActorRef>();
            router2.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            ExpectMsg(destinationC);
        }

        [Fact]
        public async Task ConsistentHashingGroupRouterMustCreateRouteesFromConfiguration()
        {
            var currentRoutees = await router3.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            currentRoutees.Members.Count().ShouldBe(3);
        }

        [Fact]
        public void ConsistentHashingGroupRouterMustSelectDestinationBasedOnConsistentHashKeyOfMessage()
        {
            router3.Tell(new Msg("a", "A"));
            var destinationA = ExpectMsg<ActorRef>();
            router3.Tell(new ConsistentHashableEnvelope("AA", "a"));
            ExpectMsg(destinationA);

            router3.Tell(new Msg(17, "A"));
            var destinationB = ExpectMsg<ActorRef>();
            router3.Tell(new ConsistentHashableEnvelope("BB", 17));
            ExpectMsg(destinationB);

            router3.Tell(new Msg(new MsgKey("c"), "C"));
            var destinationC = ExpectMsg<ActorRef>();
            router3.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            ExpectMsg(destinationC);
        }

        [Fact]
        public void ConsistentHashingGroupRouterMustSelectDestinationWithDefinedHashMapping()
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
            var router4 =
                Sys.ActorOf(Props.Empty.WithRouter(new ConsistentHashingGroup(new[]{c},hashMapping: hashMapping)), "router4");

            router4.Tell(new Msg2("a", "A"));
            var destinationA = ExpectMsg<ActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("AA", "a"));
            ExpectMsg(destinationA);

            router4.Tell(new Msg2(17, "A"));
            var destinationB = ExpectMsg<ActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("BB", 17));
            ExpectMsg(destinationB);

            router4.Tell(new Msg2(new MsgKey("c"), "C"));
            var destinationC = ExpectMsg<ActorRef>();
            router4.Tell(new ConsistentHashableEnvelope("CC", new MsgKey("c")));
            ExpectMsg(destinationC);
        }

        [Fact]
        public async Task ConsistentHashingRouterMustAdjustNodeRingWhenRouteeDies()
        {
            //create pool router with two routees
            var router5 =
                Sys.ActorOf(Props.Create<Echo>().WithRouter(new ConsistentHashingPool(2, null, null, null)), "router5");

            //verify that we have at least 2 routees
            var currentRoutees = await router5.Ask<Routees>(new GetRoutees(), GetTimeoutOrDefault(null));
            currentRoutees.Members.Count().ShouldBe(2);

            router5.Tell(new Msg("a", "A"), TestActor);
            var actorWhoDies = ExpectMsg<ActorRef>();

            //kill off the actor
            actorWhoDies.Tell(PoisonPill.Instance);

            //might take some time for the deathwatch to get processed
            AwaitAssert(() =>
            {
                router5.Tell(new Msg("a", "A"), TestActor);
                //verify that a different actor now owns this hash range
                var actorWhoDidntDie = ExpectMsg<ActorRef>(TimeSpan.FromMilliseconds(50));
                actorWhoDidntDie.ShouldNotBe(actorWhoDies);
            }, TimeSpan.FromSeconds(5));

            
        }
    }
}

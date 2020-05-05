//-----------------------------------------------------------------------
// <copyright file="RemoteRandomSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Remote.TestKit;
using Akka.Routing;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.Remote.Tests.MultiNode.Router
{
    public class RemoteRandomMultiNodeConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }

        public RemoteRandomMultiNodeConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = DebugConfig(false);

            DeployOnAll(@"
               /service-hello {
                    router = ""random-pool""
                    nr-of-instances = 3
                    target.nodes = [""@first@"", ""@second@"", ""@third@""]
                  }
           ");
        }
    }

    public class RemoteRandomSpec : MultiNodeSpec
    {
        private readonly RemoteRandomMultiNodeConfig _config;

        public RemoteRandomSpec() : this(new RemoteRandomMultiNodeConfig())
        {
        }

        protected RemoteRandomSpec(RemoteRandomMultiNodeConfig config) : base(config, typeof(RemoteRandomSpec))
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

        [MultiNodeFact]
        public void RemoteRandomSpecs()
        {
            A_remote_random_pool_must_be_locally_instantiated_on_a_remote_node_and_be_able_to_communicate_through_its_RemoteActorRef();
        }

        public void A_remote_random_pool_must_be_locally_instantiated_on_a_remote_node_and_be_able_to_communicate_through_its_RemoteActorRef()
        {
            RunOn(() =>
            {
                EnterBarrier("start", "broadcast-end", "end", "done");
            }, _config.First, _config.Second, _config.Third);

            RunOn(() =>
            {
                EnterBarrier("start");
                var actor = Sys.ActorOf(new RandomPool(0).Props(Props.Create<SomeActor>()), "service-hello");
                actor.Should().BeOfType<RoutedActorRef>();

                var connectionCount = 3;
                var iterationCount = 100;

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
                // since it's random we can't be too strict in the assert
                replies.Values.Count(x => x > 0).Should().BeGreaterThan(connectionCount - 2);
                replies.ContainsKey(Node(_config.Fourth).Address).Should().BeFalse();

                // shut down the actor before we let the other node(s) shut down so we don't try to send
                // "Terminate" to a shut down node
                Sys.Stop(actor);
                EnterBarrier("done");
            }, _config.Fourth);
        }
    }
}

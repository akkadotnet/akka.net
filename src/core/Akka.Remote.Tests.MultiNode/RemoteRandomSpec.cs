//-----------------------------------------------------------------------
// <copyright file="RemoteRandomSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.TestKit;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteRandomMultiNodeConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }
        public RoleName Third { get; private set; }
        public RoleName Fourth { get; private set; }

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

    public class RemoteRandomMultiNode1 : RemoteRandomSpec
    {
    }

    public class RemoteRandomMultiNode2 : RemoteRandomSpec
    {
    }

    public class RemoteRandomMultiNode3 : RemoteRandomSpec
    {
    }

    public class RemoteRandomMultiNode4 : RemoteRandomSpec
    {
    }

    public abstract class RemoteRandomSpec : MultiNodeSpec
    {
        private readonly RemoteRandomMultiNodeConfig _config;

        protected RemoteRandomSpec() : this(new RemoteRandomMultiNodeConfig())
        {
        }

        protected RemoteRandomSpec(RemoteRandomMultiNodeConfig config) : base(config)
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

        [MultiNodeFact]
        public void RemoteRandomSpecs()
        {
            ARemoteRandomPoolMustBeLocallyInstantiatedOnARemoteNodeAndBeAbleToCommunicateThroughItsRemoteActorRef();
        }

        public void
            ARemoteRandomPoolMustBeLocallyInstantiatedOnARemoteNodeAndBeAbleToCommunicateThroughItsRemoteActorRef()
        {
            RunOn(() => { EnterBarrier("start", "broadcast-end", "end", "done"); },
               _config.First, _config.Second, _config.Third);

            var runOnFourth = new Action(() =>
            {
                EnterBarrier("start");
                var actor = Sys.ActorOf(new RandomPool(nrOfInstances: 0)
                    .Props(Props.Create<SomeActor>()), "service-hello");

                Assert.IsType<RoutedActorRef>(actor);

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
                replies.Values.Count(x => x > 0).ShouldBeGreaterThan(connectionCount - 2);
                Assert.False(replies.ContainsKey(Node(_config.Fourth).Address));

                Sys.Stop(actor);
                EnterBarrier("done");
            });
            RunOn(runOnFourth, _config.Fourth);
        }
    }
}

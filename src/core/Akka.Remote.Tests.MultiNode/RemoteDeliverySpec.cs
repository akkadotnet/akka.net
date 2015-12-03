//-----------------------------------------------------------------------
// <copyright file="RemoteDeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Remote.TestKit;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteDeliveryMultiNetSpec : MultiNodeConfig
    {
        public RemoteDeliveryMultiNetSpec()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false);
        }

        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }
        public RoleName Third { get; private set; }

        public sealed class Letter
        {
            public Letter(int n, List<IActorRef> route)
            {
                N = n;
                Route = route;
            }

            public int N { get; private set; }
            public List<IActorRef> Route { get; private set; }
        }

        public class Postman : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                var letter = message as Letter;
                if (letter != null)
                {
                    letter.Route[0].Tell(new Letter(letter.N, letter.Route.Skip(1).ToList()));
                }
            }
        }
    }

    public class RemoteDeliveryMultiNetNode1 : RemoteDeliverySpec
    {
    }

    public class RemoteDeliveryMultiNetNode2 : RemoteDeliverySpec
    {
    }

    public class RemoteDeliveryMultiNetNode3 : RemoteDeliverySpec
    {
    }

    public class RemoteDeliverySpec : MultiNodeSpec
    {
        private readonly RemoteDeliveryMultiNetSpec _config;
        private readonly Func<RoleName, string, IActorRef> _identify;

        protected RemoteDeliverySpec() : this(new RemoteDeliveryMultiNetSpec())
        {
        }

        protected RemoteDeliverySpec(RemoteDeliveryMultiNetSpec config) : base(config)
        {
            _config = config;

            _identify = (role, actorName) => Within(TimeSpan.FromSeconds(10), () =>
                {
                    Sys.ActorSelection(Node(role)/"user"/actorName)
                        .Tell(new Identify(actorName));
                    return ExpectMsg<ActorIdentity>()
                        .Subject;
                });
        }

        protected override int InitialParticipantsValueFactory
        {
            get
            {
                return Roles.Count;
            }
        }

        [MultiNodeFact]
        public void Remoting_with_TCP_must_not_drop_messages_under_normal_circumstances()
        {
            Sys.ActorOf<RemoteDeliveryMultiNetSpec.Postman>("postman-" + Myself.Name);
            EnterBarrier("actors-started");

            RunOn(() =>
                {
                    var p1 = _identify(_config.First, "postman-first");
                    var p2 = _identify(_config.Second, "postman-second");
                    var p3 = _identify(_config.Third, "postman-third");
                    var route = new List<IActorRef>
                    {
                        p2,
                        p3,
                        p2,
                        p3,
                        TestActor
                    };

                    for (var n = 1; n <= 500; n++)
                    {
                        p1.Tell(new RemoteDeliveryMultiNetSpec.Letter(n, route));
                        var letterNumber = n;
                        ExpectMsg<RemoteDeliveryMultiNetSpec.Letter>(
                            letter => letter.N == letterNumber && letter.Route.Count == 0,
                            TimeSpan.FromSeconds(5));

                        // in case the loop count is increased it is good with some progress feedback
                        if (n%10000 == 0)
                        {
                            Log.Info("Passed [{0}]", n);
                        }
                    }
                },
                _config.First);

            EnterBarrier("after-1");
        }
    }
}
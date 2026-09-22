//-----------------------------------------------------------------------
// <copyright file="RemoteDeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.TestKit;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteDeliveryMultiNetSpec : MultiNodeConfig
    {
        public RemoteDeliveryMultiNetSpec()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(true)
                .WithFallback(ConfigurationFactory.ParseString(@"
                  # Classic (DotNetty) only; Artery has no batching switch and ignores this key.
                  akka.remote.dot-netty.tcp.batching.enabled = false # disable batching
                "));
        }

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

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

    public class RemoteDeliverySpec : MultiNodeSpec
    {
        private readonly RemoteDeliveryMultiNetSpec _config;
        private readonly Func<RoleName, string, Task<IActorRef>> _identify;

        public RemoteDeliverySpec() : this(new RemoteDeliveryMultiNetSpec())
        {
        }

        protected RemoteDeliverySpec(RemoteDeliveryMultiNetSpec config) : base(config, typeof(RemoteDeliverySpec))
        {
            _config = config;

            _identify = (role, actorName) => WithinAsync(TimeSpan.FromSeconds(10), async () =>
                {
                    // NodeAsync, not Node: Node blocks on the controller round trip, which would
                    // run before the block hands WithinAsync a task and so outside the 10s bound.
                    Sys.ActorSelection(await NodeAsync(role)/"user"/actorName)
                        .Tell(new Identify(actorName));
                    return (await ExpectMsgAsync<ActorIdentity>())
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
        public async Task Remoting_with_TCP_must_not_drop_messages_under_normal_circumstances()
        {
            Sys.ActorOf<RemoteDeliveryMultiNetSpec.Postman>("postman-" + Myself.Name);
            await EnterBarrierAsync("actors-started");

            await RunOnAsync(async () =>
                {
                    var p1 = await _identify(_config.First, "postman-first");
                    var p2 = await _identify(_config.Second, "postman-second");
                    var p3 = await _identify(_config.Third, "postman-third");
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
                        await ExpectMsgAsync<RemoteDeliveryMultiNetSpec.Letter>(
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

            // 'second' and 'third' reach this barrier about a second into the run, while 'first'
            // is still looping, and BarrierCoordinator arms a barrier's clock on the FIRST
            // arrival and only ever shortens it. So this budget, not the 5s per-letter wait, is
            // what bounds the whole 500-letter loop, and at the 30s default the loop outlives
            // its own barrier on a saturated CI agent. EnterBarrierAsync asks the coordinator
            // for RemainingOr(barrier-timeout), so a Within around it sets that budget without
            // raising akka.testconductor.barrier-timeout, which also drives the node teardown
            // poll in MultiNodeSpecAfterAll (AwaitCondition polls at max/10) and would add
            // barrier-timeout/10 of wall clock to every run. 300s over 500 letters permits a
            // 600ms mean round trip: 3x the slowest mean measured on such an agent, and far
            // under the 5s per-letter deadline, which stays the assertion that reports a real
            // drop. A node that fails still aborts the barrier at once, because losing a client
            // fails a barrier already in progress.
            await WithinAsync(TimeSpan.FromSeconds(300), () => EnterBarrierAsync("after-1"));
        }
    }
}

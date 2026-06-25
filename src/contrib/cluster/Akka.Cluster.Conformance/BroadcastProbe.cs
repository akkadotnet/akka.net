//-----------------------------------------------------------------------
// <copyright file="BroadcastProbe.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Cluster.Conformance
{
    /// <summary>
    /// A trivial routee: replies to the sender with whatever message it receives. Hosted at
    /// <c>/user/echo</c> on the reference seed (and expected at the same path on every node-under-test)
    /// so a cluster broadcast router can fan a message out to a routee on each node.
    /// </summary>
    internal sealed class EchoActor : ReceiveActor
    {
        public static Props Props => Akka.Actor.Props.Create(() => new EchoActor());

        public EchoActor()
        {
            ReceiveAny(msg => Sender.Tell(msg));
        }
    }

    /// <summary>
    /// Periodically broadcasts a ping through a cluster broadcast (Group) router whose routees are the
    /// <c>/user/echo</c> actors on each member node, and records the first reply from each remote node as
    /// a <c>RoutedReply</c> routing event. A reply from a node proves the broadcast was delivered to a
    /// routee there and handled — i.e. "a message reached that node".
    /// </summary>
    internal sealed class BroadcastCollectorActor : ReceiveActor
    {
        public sealed class Tick
        {
            public static readonly Tick Instance = new();
            private Tick() { }
        }

        public const string Ping = "ACT-BROADCAST-PING";

        private readonly ConformanceTrace _trace;
        private readonly IActorRef _router;
        private readonly HashSet<string> _recorded = new();
        private ICancelable? _timer;

        public BroadcastCollectorActor(ConformanceTrace trace, IActorRef router)
        {
            _trace = trace;
            _router = router;

            Receive<Tick>(_ => _router.Tell(Ping));

            // Any other message is a reply echoed back by a routee.
            ReceiveAny(_ =>
            {
                var addr = Sender?.Path?.Address;
                if (addr is not null && !string.IsNullOrEmpty(addr.Host) && _recorded.Add(addr.ToString()))
                    _trace.Append(ConformanceSource.Routing, ConformanceDirection.Inbound, "RoutedReply", addr, "broadcast echo");
            });
        }

        protected override void PreStart() =>
            _timer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), Self, Tick.Instance, Self);

        protected override void PostStop() => _timer?.Cancel();
    }
}

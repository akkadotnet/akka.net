using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Event;
using Samples.Cluster.ClusterClient.Messages;

namespace Samples.Cluster.ClusterClient.Client
{
    public class PingerActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly Address _seedNodeAddress;
        private IActorRef _clusterClient;

        private int _pingCounter;
        private ICancelable _pingTask;

        public PingerActor(Address seedNodeAddress)
        {
            _seedNodeAddress = seedNodeAddress;
            Receive<TryPing>(_ =>
            {
                var msg = new Ping("Hi!" + _pingCounter++);
                var timeout = TimeSpan.FromSeconds(3);
                var self = Context.Self;

                _clusterClient.Ask(new Akka.Cluster.Tools.Client.ClusterClient.Send("/user/distributor", msg), timeout)
                    .ContinueWith(tr =>
                    {
                        if (tr.IsCanceled || tr.IsFaulted)
                            return new PongTimeout(msg.Msg, timeout);
                        return tr.Result;
                    }).PipeTo(self);
            });

            Receive<Pong>(p =>
            {
                _log.Info("Reply [{0}] was successfully processed and sent by node [{1}]", p.Rsp, p.ReplyAddr);
            });

            Receive<PongTimeout>(t =>
            {
                _log.Warning("Attempt to send message [{0}] timed out after {1} - no nodes responded in time",
                    t.OriginalMsg, t.Timeout);
            });
        }

        protected override void PreStart()
        {
            // region is to make it easier to perform DocFx targeting

            #region

            // create an absolute path to the ClusterClientReceptionists we wish to contact
            var actorPaths = new List<ActorPath>
            {
                new RootActorPath(_seedNodeAddress) / "system" / "receptionist"
            }.ToImmutableHashSet();

            // start ClusterClient
            _clusterClient = Context.ActorOf(Akka.Cluster.Tools.Client.ClusterClient.Props(ClusterClientSettings
                .Create(Context.System)
                .WithInitialContacts(actorPaths)), "client");

            #endregion

            _pingTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1), Self, TryPing.Instance, ActorRefs.NoSender);
        }

        protected override void PostStop()
        {
            _pingTask.Cancel();
        }

        private class TryPing
        {
            public static readonly TryPing Instance = new TryPing();

            private TryPing()
            {
            }
        }

        private class PongTimeout
        {
            public PongTimeout(string originalMsg, TimeSpan timeout)
            {
                OriginalMsg = originalMsg;
                Timeout = timeout;
            }

            public string OriginalMsg { get; }

            public TimeSpan Timeout { get; }
        }
    }
}
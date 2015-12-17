using System.Net;
using Akka.Actor;
using Akka.Event;
using Akka.IO;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.Serialization;

namespace Akka.MultiNodeTestRunner.Shared.Logging
{
    /// <summary>
    /// Listens for incoming messages from all of the individual nodes participating in a spec
    /// and hands them off to the <see cref="MessageSinkActor"/> associated with this spec.
    /// </summary>
    public class UdpLogCollector : ReceiveActor, IWithUnboundedStash
    {
        #region Message classes

        public class GetLocalAddress
        {
            public static readonly GetLocalAddress Instance = new GetLocalAddress();
            private GetLocalAddress() { }
        }

        #endregion 

        private EndPoint _localAddress;
        private readonly IActorRef _messageSinkActor;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        

        public UdpLogCollector(IActorRef messageSinkActor)
        {
            _messageSinkActor = messageSinkActor;
            Unbound();
        }

        private void Unbound()
        {
            Receive<Udp.Bound>(bound =>
            {
                _localAddress = bound.LocalAddress;
                _log.Info("connected and listening to inbound MultiNode messages on {0}", bound.LocalAddress);
                BecomeBound();
            });

            ReceiveAny(o => Stash.Stash());
        }

        private void BecomeBound()
        {
            Stash.UnstashAll();
            Become(Bound);
        }

        private void Bound()
        {
            Receive<GetLocalAddress>(local => Sender.Tell(_localAddress));

            Receive<UdpConnected.CommandFailed>(failed =>
            {
                _log.Error(failed.Cmd.FailureMessage.ToString());
            });

            Receive<Udp.Received>(received =>
            {
                var obj = received.Data.DecodeString();
                _log.Info(obj.ToString());
                _messageSinkActor.Forward(obj);
            });
        }

        public IStash Stash { get; set; }
    }
}

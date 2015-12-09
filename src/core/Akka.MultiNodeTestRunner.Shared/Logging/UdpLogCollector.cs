using Akka.Actor;
using Akka.Event;
using Akka.IO;
using Akka.MultiNodeTestRunner.Shared.Sinks;

namespace Akka.MultiNodeTestRunner.Shared.Logging
{
    /// <summary>
    /// Listens for incoming messages from all of the individual nodes participating in a spec
    /// and hands them off to the <see cref="MessageSinkActor"/> associated with this spec.
    /// </summary>
    public class UdpLogCollector : ReceiveActor, IWithUnboundedStash
    {
        private IActorRef _server;
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
                _server = Sender;
                _log.Info("connected and listening to inbound MultiNode messages on {0}", bound.LocalAddress);
                BecomeBound();
            });

            ReceiveAny(o => Stash.Stash());
        }

        private void BecomeBound()
        {
            Become(Bound);
        }

        private void Bound()
        {
            Receive<Udp.Received>(received =>
            {

            });
        }


        public IStash Stash { get; set; }
    }
}

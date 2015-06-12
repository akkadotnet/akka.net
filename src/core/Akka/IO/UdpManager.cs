using System;
using Akka.Actor;

namespace Akka.IO
{
    /**
     * TODO: CLRify comment
     * 
     * INTERNAL API
     *
     * UdpManager is a facade for simple fire-and-forget style UDP operations
     *
     * UdpManager is obtainable by calling {{{ IO(Udp) }}} (see [[akka.io.IO]] and [[akka.io.Udp]])
     *
     * *Warning!* Udp uses [[java.nio.channels.DatagramChannel#send]] to deliver datagrams, and as a consequence if a
     * security manager  has been installed then for each datagram it will verify if the target address and port number are
     * permitted. If this performance overhead is undesirable use the connection style Udp extension.
     *
     * == Bind and send ==
     *
     * To bind and listen to a local address, a [[akka.io.Udp..Bind]] command must be sent to this actor. If the binding
     * was successful, the sender of the [[akka.io.Udp.Bind]] will be notified with a [[akka.io.Udp.Bound]]
     * message. The sender of the [[akka.io.Udp.Bound]] message is the Listener actor (an internal actor responsible for
     * listening to server events). To unbind the port an [[akka.io.Tcp.Unbind]] message must be sent to the Listener actor.
     *
     * If the bind request is rejected because the Udp system is not able to register more channels (see the nr-of-selectors
     * and max-channels configuration options in the akka.io.udp section of the configuration) the sender will be notified
     * with a [[akka.io.Udp.CommandFailed]] message. This message contains the original command for reference.
     *
     * The handler provided in the [[akka.io.Udp.Bind]] message will receive inbound datagrams to the bound port
     * wrapped in [[akka.io.Udp.Received]] messages which contain the payload of the datagram and the sender address.
     *
     * UDP datagrams can be sent by sending [[akka.io.Udp.Send]] messages to the Listener actor. The sender port of the
     * outbound datagram will be the port to which the Listener is bound.
     *
     * == Simple send ==
     *
     * Udp provides a simple method of sending UDP datagrams if no reply is expected. To acquire the Sender actor
     * a SimpleSend message has to be sent to the manager. The sender of the command will be notified by a SimpleSenderReady
     * message that the service is available. UDP datagrams can be sent by sending [[akka.io.Udp.Send]] messages to the
     * sender of SimpleSenderReady. All the datagrams will contain an ephemeral local port as sender and answers will be
     * discarded.
     *
     */

    internal class UdpManager : SelectionHandler.SelectorBasedManager
    {
        private readonly UdpExt _udp;

        public UdpManager(UdpExt udp) 
            : base(udp.Setting, udp.Setting.NrOfSelectors)
        {
            _udp = udp;
        }

        protected override bool Receive(object m)
        {
            return WorkerForCommandHandler(message =>
            {
                var b = message as Udp.Bind;
                if (b != null)
                {
                    var commander = Sender;
                    return registry => Props.Create(() => new UdpListener(_udp, registry, commander, b));
                }
                var s = message as Udp.SimpleSender;
                if (s != null)
                {
                    var commander = Sender;
                    return registry => Props.Create(() => new UdpSender(_udp, registry, commander, s.Options));
                }
                throw new Exception();
            })(m);
        }
    }
}

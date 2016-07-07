//-----------------------------------------------------------------------
// <copyright file="TransportManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;

namespace Akka.Remote.Transport.AkkaIO
{
    //Commands
    internal class Associate
    {
        public Associate(Address remoteAddress)
        {
            RemoteAddress = remoteAddress;
        }

        public Address RemoteAddress { get; private set; }
    }

    internal class Listen
    {
        public Listen(string hostname, int port)
        {
            Hostname = hostname;
            Port = port;
        }

        public string Hostname { get; private set; }
        public int Port { get; private set; }
    }

    //TODO: Supervision. Stopping Strategy is probably the only option. Do we need to signal remoting?
    internal class TransportManager : UntypedActor, IWithUnboundedStash
    {
        public IStash Stash { get; set; }

        protected override void OnReceive(object message)
        {
            if (message is Associate)
            {
                var associate = message as Associate;
                Context.System.Tcp().Tell(new Tcp.Connect(associate.RemoteAddress.ToEndpoint()));
                BecomeStacked(WaitingForConnected(Sender, associate));
            }
            else if (message is Listen)
            {
                var listen = message as Listen;
                var handler = Context.ActorOf(Props.Create(() => new TransportListener()));
                Context.System.Tcp().Tell(new Tcp.Bind(handler, new IPEndPoint(IPAddress.Any, listen.Port)));
                BecomeStacked(WaitingForBound(Sender, handler, listen));
            }
            else
            {
                Unhandled(message);
            }
        }

        private Receive WaitingForBound(IActorRef replyTo, IActorRef handler, Listen listen)
        {
            return message =>
            {
                if (message is Tcp.Bound)
                {
                    var bound = message as Tcp.Bound;
                    var promise = new TaskCompletionSource<IAssociationEventListener>();
                    promise.Task.PipeTo(handler);
                    replyTo.Tell(
                        Tuple.Create(
                            new Address(AkkaIOTransport.Protocal, Context.System.Name, listen.Hostname,
                                ((IPEndPoint) bound.LocalAddress).Port), promise));
                    UnbecomeStacked();
                    Stash.Unstash();
                    return true;
                }
                return Queue(message);
            };
        }

        private Receive WaitingForConnected(IActorRef replyTo, Associate associate)
        {
            return message =>
            {
                if (message is Tcp.Connected)
                {
                    var connected = message as Tcp.Connected;
                    var handler = Context.ActorOf(Props.Create(() => new ConnectionAssociationActor(Sender)));
                    Sender.Tell(new Tcp.Register(handler));
                    replyTo.Tell(new ConnectionAssociationHandle(handler,
                        connected.LocalAddress.ToAddress(Context.System), associate.RemoteAddress));
                    UnbecomeStacked();
                    Stash.Unstash();
                    return true;
                }
                if (message is Tcp.CommandFailed)
                {
                    //TODO: Handle
                    return true;
                }
                return Queue(message);
            };
        }

        private bool Queue(object message)
        {
            if (message is Associate || message is Listen)
            {
                // We only process one connect at a time, if we receive more associate or listener 
                // requests during this time we just queue them. 
                Stash.Stash();
                return true;
            }
            return false;
        }
    }
}
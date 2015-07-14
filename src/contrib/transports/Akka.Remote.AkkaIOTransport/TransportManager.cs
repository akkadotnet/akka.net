//-----------------------------------------------------------------------
// <copyright file="ActorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Remote.Transport;

namespace Akka.Remote.AkkaIOTransport
{
    //Commands
    class Associate
    {
        public Address RemoteAddress { get; private set; }

        public Associate(Address remoteAddress)
        {
            RemoteAddress = remoteAddress;
        }
    }
    class Listen
    {
        public Listen(int port)
        {
            Port = port;
        }
        public int Port { get; private set; }
    }

    //TODO: Supervision. Stopping Strategy is probably the only option. Do we need to signal remoting?
    class TransportManager : UntypedActor, IWithUnboundedStash
    {
        protected override void OnReceive(object message)
        {
            if (message is Associate)
            {
                var associate = message as Associate;
                Context.System.Tcp().Tell(new Tcp.Connect(associate.RemoteAddress.ToEndpoint()));
                BecomeStacked(WaitingForConnected(Sender));
            }
            else if (message is Listen)
            {
                var listen = message as Listen;
                var handler = Context.ActorOf(Props.Create(() => new TransportListener()));
                Context.System.Tcp().Tell(new Tcp.Bind(handler, new IPEndPoint(IPAddress.Loopback, listen.Port)));
                BecomeStacked(WaitingForBound(Sender, handler));
            }
            else
            {
                Unhandled(message);
            }
        }

        private Receive WaitingForBound(IActorRef replyTo, IActorRef handler)
        {
            return message =>
            {
                if (message is Tcp.Bound)
                {
                    var bound = message as Tcp.Bound;
                    var promise = new TaskCompletionSource<IAssociationEventListener>();
                    promise.Task.PipeTo(handler);
                    replyTo.Tell(Tuple.Create(bound.LocalAddress.ToAddress(Context.System), promise));
                    UnbecomeStacked();
                    Stash.Unstash();
                    return true;
                }
                return Queue(message);
            };
        }

        private Receive WaitingForConnected(IActorRef replyTo)
        {
            return message =>
            {
                if (message is Tcp.Connected)
                {
                    var connected = message as Tcp.Connected;
                    var handler = Context.ActorOf(Props.Create(() => new ConnectionAssociationActor(Sender)));
                    Sender.Tell(new Tcp.Register(handler));
                    replyTo.Tell(new ConnectionAssociationHandle(handler, connected.LocalAddress.ToAddress(Context.System), connected.RemoteAddress.ToAddress(Context.System)));
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
                // We only process one connect at a time, if we receive more associate or listner 
                // requests during this time we just queue them. 
                Stash.Stash();
                return true;
            }
            return false;
        }

        public IStash Stash { get; set; }
    }
}
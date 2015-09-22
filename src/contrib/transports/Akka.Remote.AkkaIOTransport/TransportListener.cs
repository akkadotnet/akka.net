//-----------------------------------------------------------------------
// <copyright file="TransportListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.IO;
using Akka.Remote.Transport;

namespace Akka.Remote.AkkaIOTransport
{
    internal class TransportListener : UntypedActor, IWithUnboundedStash
    {
        protected override void OnReceive(object message)
        {
            if (message is IAssociationEventListener)
            {
                var listener = message as IAssociationEventListener;
                Become(Accepting(listener));
                Stash.UnstashAll();
            }
            else
            {
                Stash.Stash();
            }
        }

        private Receive Accepting(IAssociationEventListener listener)
        {
            return message =>
            {
                var connected = message as Tcp.Connected;
                if (connected != null)
                {
                    var actor = Context.ActorOf(Props.Create(() => new ConnectionAssociationActor(Sender)));
                    var association = new ConnectionAssociationHandle(actor, connected.LocalAddress.ToAddress(Context.System),
                                                                             connected.RemoteAddress.ToAddress(Context.System));
                    listener.Notify(new InboundAssociation(association));
                }
                return false;
            };
        }

        public IStash Stash { get; set; }
    }
}
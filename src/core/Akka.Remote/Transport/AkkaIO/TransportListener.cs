//-----------------------------------------------------------------------
// <copyright file="TransportListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.IO;

namespace Akka.Remote.Transport.AkkaIO
{
    internal class TransportListener : UntypedActor, IWithUnboundedStash
    {
        public IStash Stash { get; set; }

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
                    var association = new ConnectionAssociationHandle(actor,
                        connected.LocalAddress.ToAddress(Context.System),
                        connected.RemoteAddress.ToAddress(Context.System));
                    listener.Notify(new InboundAssociation(association));
                }
                return false;
            };
        }
    }
}
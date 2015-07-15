//-----------------------------------------------------------------------
// <copyright file="ConnectionAssociation.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Remote.Transport;
using ByteString = Google.ProtocolBuffers.ByteString;

namespace Akka.Remote.AkkaIOTransport
{
    class ConnectionAssociationHandle : AssociationHandle
    {
        private readonly IActorRef _connection;

        public ConnectionAssociationHandle(IActorRef connection, Address localAddress, Address remoteAddress)
            : base(localAddress, remoteAddress)
        {
            _connection = connection;
            ReadHandlerSource = new TaskCompletionSource<IHandleEventListener>();
            ReadHandlerSource.Task.PipeTo(connection);
        }

        public override bool Write(ByteString payload)
        {
            _connection.Tell(payload);
            return true;
        }

        public override void Disassociate()
        {
            //TODO: Should we close connection?
            //_connection.Tell(Tcp.Close.Instance);
        }
    }

    class ConnectionAssociationActor : UntypedActor, IWithUnboundedStash
    {
        private readonly IActorRef _connection;

        public ConnectionAssociationActor(IActorRef connection)
        {
            _connection = connection;
            connection.Tell(new Tcp.Register(Self));
        }

        protected override void OnReceive(object message)
        {
            if (message is IHandleEventListener)
            {
                var el = message as IHandleEventListener;
                Context.Become(Receiving(el));
                Stash.UnstashAll();
            }
            else
            {
                Stash.Stash();
            }
        }

        private UntypedReceive Receiving(IHandleEventListener el)
        {
            return message =>
            {
                if (message is Tcp.Received)
                {
                    var received = message as Tcp.Received;
                    el.Notify(new InboundPayload(ByteString.CopyFrom(received.Data.ToArray())));
                }
                if (message is ByteString)
                {
                    var bs = message as ByteString;
                    _connection.Tell(Tcp.Write.Create(IO.ByteString.Create(bs.ToByteArray())));
                }
                else
                {
                    Unhandled(message);
                }
            };
        }

        public IStash Stash { get; set; }
    }
}
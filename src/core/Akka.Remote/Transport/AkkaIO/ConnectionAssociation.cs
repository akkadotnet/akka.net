//-----------------------------------------------------------------------
// <copyright file="ConnectionAssociation.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using ByteString = Google.ProtocolBuffers.ByteString;

namespace Akka.Remote.Transport.AkkaIO
{
    internal class ConnectionAssociationHandle : AssociationHandle
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

    internal class ConnectionAssociationActor : UntypedActor, IWithUnboundedStash
    {
        private readonly IActorRef _connection;

        public ConnectionAssociationActor(IActorRef connection)
        {
            _connection = connection;
            connection.Tell(new Tcp.Register(Self));
        }

        public IStash Stash { get; set; }

        protected override void OnReceive(object message)
        {
            if (message is IHandleEventListener)
            {
                var el = message as IHandleEventListener;
                Context.Become(WaitingForPrefix(el, IO.ByteString.Empty));
                Stash.UnstashAll();
            }
            else
            {
                Stash.Stash();
            }
        }

        private UntypedReceive WaitingForPrefix(IHandleEventListener el, IO.ByteString buffer)
        {
            if (buffer.Count >= 4)
            {
                var length = buffer.Iterator().GetInt();
                return WaitingForBody(el, buffer.Drop(4), length);
            }
            return message =>
            {
                if (message is Tcp.Received)
                {
                    var received = message as Tcp.Received;
                    Become(WaitingForPrefix(el, buffer.Concat(received.Data)));
                }
                else HandleWrite(message);
            };
        }

        private UntypedReceive WaitingForBody(IHandleEventListener el, IO.ByteString buffer, int length)
        {
            if (buffer.Count >= length)
            {
                var parts = buffer.SplitAt(length);
                el.Notify(new InboundPayload(ByteString.CopyFrom(parts.Item1.ToArray())));
                return WaitingForPrefix(el, parts.Item2);
            }
            return message =>
            {
                if (message is Tcp.Received)
                {
                    var received = message as Tcp.Received;
                    Become(WaitingForBody(el, buffer.Concat(received.Data), length));
                }
                else HandleWrite(message);
            };
        }

        private void HandleWrite(object message)
        {
            if (message is ByteString)
            {
                var bs = message as ByteString;
                var buffer = ByteString.Unsafe.GetBuffer(bs);
                var builder = new ByteStringBuilder();
                builder.PutInt(buffer.Length, ByteOrder.BigEndian);
                builder.PutBytes(buffer);
                _connection.Tell(Tcp.Write.Create(builder.Result()));
            }
            else Unhandled(message);
        }
    }
}
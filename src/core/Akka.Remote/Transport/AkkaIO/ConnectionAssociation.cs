//-----------------------------------------------------------------------
// <copyright file="ConnectionAssociation.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using ByteString = Google.Protobuf.ByteString;

namespace Akka.Remote.Transport.AkkaIO
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class ConnectionAssociationHandle : AssociationHandle
    {
        private readonly IActorRef _connection;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        public ConnectionAssociationHandle(IActorRef connection, Address localAddress, Address remoteAddress)
            : base(localAddress, remoteAddress)
        {
            _connection = connection;
            ReadHandlerSource = new TaskCompletionSource<IHandleEventListener>();
            ReadHandlerSource.Task.PipeTo(connection);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <returns>TBD</returns>
        public override bool Write(ByteString payload)
        {
            _connection.Tell(payload);
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Disassociate()
        {
            //TODO: Should we close connection?
            //_connection.Tell(Tcp.Close.Instance);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class ConnectionAssociationActor : UntypedActor, IWithUnboundedStash
    {
        private readonly IActorRef _connection;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        public ConnectionAssociationActor(IActorRef connection)
        {
            _connection = connection;
            connection.Tell(new Tcp.Register(Self));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IStash Stash { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
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
                var buffer = bs.ToByteArray();
                // TODO performance, was:
                // var buffer = ByteString.Unsafe.GetBuffer(bs);
                var builder = new ByteStringBuilder();
                builder.PutInt(buffer.Length, ByteOrder.BigEndian);
                builder.PutBytes(buffer);
                _connection.Tell(Tcp.Write.Create(builder.Result()));
            }
            else Unhandled(message);
        }
    }
}
#endif
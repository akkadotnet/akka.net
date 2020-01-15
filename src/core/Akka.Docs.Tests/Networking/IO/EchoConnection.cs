//-----------------------------------------------------------------------
// <copyright file="EchoConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.IO;
using Akka.Util.Internal;

namespace DocsExamples.Networking.IO
{
    public class EchoConnection : UntypedActor
    {
        private readonly IActorRef _connection;

        public EchoConnection(IActorRef connection)
        {
            _connection = connection;
        }

        protected override void OnReceive(object message)
        {
            if (message is Tcp.Received)
            {
                var received = message as Tcp.Received;
                if (received.Data[0] == 'x')
                    Context.Stop(Self);
                else
                    _connection.Tell(Tcp.Write.Create(received.Data));
            }
            else Unhandled(message);
        }
    }
}

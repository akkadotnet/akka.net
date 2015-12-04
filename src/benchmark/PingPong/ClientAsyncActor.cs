//-----------------------------------------------------------------------
// <copyright file="ClientReceiveActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

#pragma warning disable 1998 //async method lacks an await

namespace PingPong
{
    public class ClientAsyncActor : ReceiveActor
    {
        public ClientAsyncActor(IActorRef actor, long repeat, TaskCompletionSource<bool> latch)
        {
            var received = 0L;
            var sent = 0L;
            Receive<Messages.Msg>(async m =>
            {
                received++;
                if (sent < repeat)
                {
                    actor.Tell(m);
                    sent++;
                }
                else if (received >= repeat)
                {
                    latch.SetResult(true);
                }
            });
            Receive<Messages.Run>(r =>
            {
                var msg = new Messages.Msg();
                for (int i = 0; i < Math.Min(1000, repeat); i++)
                {
                    actor.Tell(msg);
                    sent++;
                }
            });
            Receive<Messages.Started>(s => Sender.Tell(s));
        }
    }
}


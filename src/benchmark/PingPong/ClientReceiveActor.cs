//-----------------------------------------------------------------------
// <copyright file="ClientReceiveActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace PingPong
{
    public class ClientReceiveActor : ReceiveActor
    {
        public ClientReceiveActor(IActorRef actor, long repeat, TaskCompletionSource<bool> latch)
        {
            var received=0L;
            var sent=0L;
            Receive<Messages.Msg>(m =>
            {
                received++;
                if(sent < repeat)
                {
                    actor.Tell(m);
                    sent++;
                }
                else if(received >= repeat)
                {
                    latch.SetResult(true);
                }
            });
            Receive<Messages.Run>(r =>
            {
                var msg = new Messages.Msg();
                for(int i = 0; i < Math.Min(1000, repeat); i++)
                {
                    actor.Tell(msg);
                    sent++;
                }
            });
            Receive<Messages.Started>(s => Sender.Tell(s));
        }
    }
}


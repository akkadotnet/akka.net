using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace PingPong
{
    public class ClientReceiveActor : ReceiveActor
    {
        public ClientReceiveActor(ActorRef actor, long repeat, TaskCompletionSource<bool> latch)
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
#region akka-aspnet-core-worker
using Akka.Actor;

namespace Akka.AspNetCore
{
    public class Worker : ReceiveActor
    {
        public Worker()
        {
            ReceiveAny(message => 
            {
                // do your work here. Call a database, call a REST API, send message to another Actor
                // Whatever you wish to, the digital world is yours!
                switch (message)
                {
                    case "get":
                        Sender.Tell(new string[] { "value1", "value2" });
                        break;
                    default:
                        // do something
                        break;
                }
            });
        }
        public static Props Prop()
        {
            return Props.Create<Worker>();
        }
    }
}
#endregion

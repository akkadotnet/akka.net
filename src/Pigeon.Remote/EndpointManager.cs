using Pigeon.Actor;
using Pigeon.Configuration;
using Pigeon.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public class EndpointManager : UntypedActor
    {
        private Config config;
        private LoggingAdapter log;

        public EndpointManager(Configuration.Config config, Event.LoggingAdapter log)
        {
            this.config = config;
            this.log = log;
        }
        protected override void OnReceive(object message)
        {
            ReceiveBuilder.Match(message)
                .With<Listen>(m => 
                {
                    var res = Listens();
                    Sender.Tell(res);
                });

        }

        private object Listens()
        {
            return "hello";
        }
    }
}

using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public class EndpointManager : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            ReceiveBuilder.Match(message)
                .With<Listen>(m => 
                {

                });

        }
    }
}

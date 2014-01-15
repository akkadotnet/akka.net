using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public struct Envelope
    {
        public ActorRef Sender { get; set; }
        public object Message { get; set; }
    }
}

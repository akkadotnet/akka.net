using Pigeon.SignalR;
using Microsoft.AspNet.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Actor;
using System.Collections.Concurrent;

namespace Pigeon.Actor
{
    public class ActorSystem : ActorContext, IDisposable
    {       
        public ActorSystem()
        {
        }

        public override ActorSystem System
        {
            get
            {
                return this;
            }
            set
            {
                throw new NotSupportedException("Can't set the system of a system");
            }
        }

        public void Dispose()
        {
        }
    }
}
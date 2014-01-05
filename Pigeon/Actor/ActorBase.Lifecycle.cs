using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Reactive.Linq;
using Pigeon.Messaging;
using System.Threading;

namespace Pigeon.Actor
{
    public abstract partial class ActorBase
    {
        protected virtual void PreStart()
        {
        }

		protected virtual void PostRestart(Exception reason, object message)
        {
			foreach(var child in Context.GetChildren())
            {
                Context.Unwatch(child);
            //    Context.Stop(child);
            }
        }

        protected void PostStop()
        {
            //Watchers.Tell(new Terminated());
        }
    }
}

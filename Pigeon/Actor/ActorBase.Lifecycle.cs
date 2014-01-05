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
		public void AroundPreStart()
        {
            PreStart();
        }
        protected virtual void PreStart()
        {
        }

		public void AroundPostRestart(Exception reason, object message)
        {
            foreach (var child in Context.GetChildren())
            {
                Context.Unwatch(child);
                child.Stop();
            }
            PostRestart(reason, message);
        }
		protected virtual void PostRestart(Exception reason, object message)
        {
			
        }

		public void AroundPostStop()
        {
            PostStop();
        }

        protected virtual void PostStop()
        {
            //Watchers.Tell(new Terminated());
        }
    }
}

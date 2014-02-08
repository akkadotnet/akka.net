using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public void AroundPostRestart(Exception cause, object message)
        {
            //foreach (var child in Context.GetChildren())
            //{
            //    Context.Unwatch(child);
            //    child.Stop();
            //}
            PostRestart(cause, message);
        }

        protected virtual void PreRestart(Exception cause, object message)
        {

        }
        protected virtual void PostRestart(Exception cause, object message)
        {
            Context.GetChildren().ToList().ForEach(c => {
                Context.Unwatch(c);
                Context.Stop(c);
            });
            PostStop();
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

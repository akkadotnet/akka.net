using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Akka.Actor
{
    public abstract partial class ActorBase
    {
        public void AroundPreRestart(Exception cause,object message)
        {
            PreRestart(cause, message);
        }

		public void AroundPreStart()
        {
            PreStart();
        }

        protected virtual void PreStart()
        {
        }

        public void AroundPostRestart(Exception cause, object message)
        {
            PostRestart(cause);
        }

        protected virtual void PreRestart(Exception cause, object message)
        {
            Context.GetChildren().ToList().ForEach(c =>
            {
                Context.Unwatch(c);
                Context.Stop(c);
            });
            PostStop();
        }
        protected virtual void PostRestart(Exception cause)
        {
            PreStart();
        }

		public void AroundPostStop()
        {
            PostStop();
        }

        protected virtual void PostStop()
        {
            
        }
    }
}

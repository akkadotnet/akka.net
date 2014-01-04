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

		protected virtual void PostRestart()
        {
        }

        protected void PostStop()
        {
            Watchers.Tell(new Terminated());
        }
    }
}

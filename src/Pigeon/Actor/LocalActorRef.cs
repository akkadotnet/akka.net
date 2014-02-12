using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Actor
{
    public class LocalActorRef : ActorRef
    {
        public ActorCell Cell { get; private set; }

        public ActorRefProvider Provider
        {
            get
            {
                return Cell.System.Provider;
            }
        }

        public override void Stop()
        {
            Cell.Stop();
        }

        public void Suspend()
        {
            Cell.Suspend();
        }

        public override void Resume(Exception causedByFailure = null)
        {
            Cell.Resume(causedByFailure);
        }

        public void Restart()
        {
            Cell.Restart();
        }

        public LocalActorRef(ActorPath path,ActorCell context)
        {
            this.Path = path;
            this.Cell = context;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            this.Cell.Post(sender, message);
        }

        
    }
}

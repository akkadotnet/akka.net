using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Actor
{
    public class LocalActorRef : ActorRefWithCell
    {      
        public ActorRefProvider Provider
        {
            get
            {
                return Cell.System.Provider;
            }
        }

        public InternalActorRef Parent
        {
            get
            {
                return this.Cell.Parent;
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

        public override IEnumerable<ActorRef> Children
        {
            get 
            { 
                return Cell.GetChildren();
            }
        }

        public override InternalActorRef GetSingleChild(string name)
        {
            return Cell.Child(name);
        }

        public override ActorRef GetChild(IEnumerable<string> name)
        {
            var current = (ActorRef)this;
            int index = 0;
            foreach (var element in name)
            {
                index++;
                if (current is LocalActorRef)
                {
                    if (element == "..")
                    {
                        current = current.AsInstanceOf<LocalActorRef>().Parent;
                    }
                    else if (element == "")
                    {

                    }
                    else
                    {
                        current = current.AsInstanceOf<LocalActorRef>().GetSingleChild(element);
                    }
                }
                else if (current is InternalActorRef)
                {
                    var rest = name.Skip(index).ToArray();
                    return current.AsInstanceOf<InternalActorRef>().GetChild(rest);
                }
                else
                {
                    throw new NotSupportedException("Bug, we should not get here");
                }
                
            }
            return current;
        }
    }
}

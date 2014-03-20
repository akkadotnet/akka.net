using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Actor
{
    public class LocalActorRef : ActorRefWithCell
    {
        public LocalActorRef(ActorPath path, ActorCell context)
        {
            Path = path;
            Cell = context;
        }

        public override ActorRefProvider Provider
        {
            get { return Cell.System.Provider; }
        }

        public override InternalActorRef Parent
        {
            get { return Cell.Parent; }
        }

        public override IEnumerable<ActorRef> Children
        {
            get { return Cell.GetChildren(); }
        }

        public override void Stop()
        {
            Cell.Stop();
        }

        public override void Suspend()
        {
            Cell.Suspend();
        }

        public override void Resume(Exception causedByFailure = null)
        {
            Cell.Resume(causedByFailure);
        }

        public override void Restart(Exception cause)
        {
            Cell.Restart(cause);
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            Cell.Post(sender, message);
        }

        public override InternalActorRef GetSingleChild(string name)
        {
            return Cell.Child(name);
        }

        public override ActorRef GetChild(IEnumerable<string> name)
        {
            var current = (ActorRef) this;
            int index = 0;
            foreach (string element in name)
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
                else if (current != null)
                {
                    string[] rest = name.Skip(index).ToArray();
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
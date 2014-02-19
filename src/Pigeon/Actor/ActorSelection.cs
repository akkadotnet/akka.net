using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class BrokenActorSelection : BroadcastActorRef
    {
        public BrokenActorSelection(ActorPath path, params ActorRef[] actors) :base(actors)
        {
            this.Path = path;
        }
    }

    public class ActorSelection : ActorRef
    {
        private LocalActorRef localActorRef;
        private SelectionPathElement[] selectionPathElement;

        public ActorRef Anchor { get; private set; }
        public SelectionPathElement[] Path { get; set; }

        public ActorSelection(ActorRef anchor,SelectionPathElement[] path)
        {
            this.Anchor = anchor;
            this.Path = path;
        }

        public ActorSelection(ActorRef anchor, string path) : this(anchor,path.Split('/'))
        {            
        }

        public ActorSelection(ActorRef anchor, IEnumerable<string> elements)
        {
            Anchor = anchor;
            Path = elements.Select<string, SelectionPathElement>(e =>
            {
                if (e == "..")
                    return new SelectParent();
                else if (e.Contains("?") || e.Contains("*"))
                    return new SelectChildPattern(e);
                return new SelectChildName(e);
            }).ToArray();
        }


        protected override void TellInternal(object message, ActorRef sender)
        {
            Deliver(message, sender, 0, Anchor);
        }

        private void Deliver(object message, ActorRef sender,int pathIndex,ActorRef current)
        {
            if (pathIndex == Path.Length)
            {
                current.Tell(message, sender);
            }
            else
            {
                var element = this.Path[pathIndex];
                if (current is ActorRefWithCell)
                {
                    var withCell = (ActorRefWithCell)current;
                    if (element is SelectParent)
                        Deliver(message, sender, pathIndex + 1, withCell.Parent);
                    else if (element is SelectChildName)
                        Deliver(message, sender, pathIndex + 1, withCell.GetSingleChild(element.AsInstanceOf<SelectChildName>().Name));
                    else
                    {
                        //pattern, ignore for now
                    }
                }
                else
                {
                    var rest = Path.Skip(pathIndex).ToArray();
                    current.Tell(new ActorSelectionMessage(message,rest),sender);
                }
            }
        }
    }

    public class ActorSelectionMessage : AutoReceivedMessage
    {
        public ActorSelectionMessage(object message,SelectionPathElement[] elements)
        {
            this.Message = message;
            this.Elements = elements;
        }

        public object Message { get;private set; }

        public SelectionPathElement[] Elements { get; private set; }
    }

    public abstract class SelectionPathElement
    {

    }

    public class SelectChildName : SelectionPathElement
    {
        public SelectChildName(string name)
        {
            this.Name = name;
        }

        public string Name { get;private set; }

        public override string ToString()
        {
            return this.Name;
        }
    }

    public class Pattern
    {

    }

    public static class Helpers
    {
        public static Pattern MakePattern(string patternStr)
        {
            return new Pattern();
        }
    }
    public class SelectChildPattern : SelectionPathElement
    {
        public SelectChildPattern(string patternStr)
        {
            this.Pattern = Helpers.MakePattern(patternStr);
        }

        public Pattern Pattern { get; private set; }

        public override string ToString()
        {
            return Pattern.ToString();
        }
    }


    public class SelectParent : SelectionPathElement
    {
        public override string ToString()
        {
            return "..";
        }
    }
}

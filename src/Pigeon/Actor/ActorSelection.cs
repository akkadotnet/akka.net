using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorSelection : ICanTell
    {
        public ActorRef Anchor { get; private set; }
        public SelectionPathElement[] Elements { get; set; }

        public ActorSelection() { }
        public ActorSelection(ActorRef anchor,SelectionPathElement[] path)
        {
            this.Anchor = anchor;
            this.Elements = path;
        }

        public ActorSelection(ActorRef anchor, string path) : this(anchor,path == "" ? new string[]{} : path.Split('/'))
        {            
        }

        public ActorSelection(ActorRef anchor, IEnumerable<string> elements)
        {
            Anchor = anchor;
            Elements = elements.Select<string, SelectionPathElement>(e =>
            {
                if (e == "..")
                    return new SelectParent();
                else if (e.Contains("?") || e.Contains("*"))
                    return new SelectChildPattern(e);
                return new SelectChildName(e);
            }).ToArray();
        }

        public void Tell(object message)
        {
            var sender = ActorRef.NoSender;
            if (ActorCell.Current != null && ActorCell.Current.Self != null)
                sender = ActorCell.Current.Self;

            Deliver(message, sender, 0, Anchor);
        }
        public void Tell(object message, ActorRef sender)
        {
            Deliver(message, sender, 0, Anchor);
        }

        private void Deliver(object message, ActorRef sender,int pathIndex,ActorRef current)
        {
            if (pathIndex == Elements.Length)
            {
                current.Tell(message, sender);
            }
            else
            {
                var element = this.Elements[pathIndex];
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
                    var rest = Elements.Skip(pathIndex).ToArray();
                    current.Tell(new ActorSelectionMessage(message,rest),sender);
                }
            }
        }
    }

    public class ActorSelectionMessage : AutoReceivedMessage
    {
        public ActorSelectionMessage()
        {
        }

        public ActorSelectionMessage(object message,SelectionPathElement[] elements)
        {
            this.Message = message;
            this.Elements = elements;
        }

        public object Message { get; set; }

        public SelectionPathElement[] Elements { get;  set; }
    }

    public abstract class SelectionPathElement
    {

    }

    public class SelectChildName : SelectionPathElement
    {
        public SelectChildName() 
        { 
        }
        public SelectChildName(string name)
        {
            this.Name = name;
        }

        public string Name { get; set; }

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

        public Pattern Pattern { get; set; }

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

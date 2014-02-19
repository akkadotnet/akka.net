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

    public class ActorSelection
    {
        public ActorRef Anchor { get; private set; }
        public IEnumerable<SelectionPathElement> Path { get; set; }

        public static ActorSelection Apply(ActorRef anchor,string path)
        {
            return Apply(anchor, path.Split('/'));
        }

        public static ActorSelection Apply(ActorRef anchor, IEnumerable<string> elements)
        {
            return new ActorSelection
            {
                Anchor = anchor,
                Path = elements.Select<string, SelectionPathElement>(e =>
                {
                    if (e == "..")
                        return new SelectParent();
                    else if (e.Contains("?") || e.Contains("*"))
                        return new SelectChildPattern(e);
                    return new SelectChildName(e);
                })
            };
        }
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

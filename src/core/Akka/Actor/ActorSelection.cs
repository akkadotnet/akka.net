using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{      
    /// <summary>
    ///     Class ActorSelection.
    /// </summary>
    public class ActorSelection : ICanTell
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        public ActorSelection()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="path">The path.</param>
        public ActorSelection(ActorRef anchor, SelectionPathElement[] path)
        {
            Anchor = anchor;
            Elements = path;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="path">The path.</param>
        public ActorSelection(ActorRef anchor, string path)
            : this(anchor, path == "" ? new string[] {} : path.Split('/'))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="elements">The elements.</param>
        public ActorSelection(ActorRef anchor, IEnumerable<string> elements)
        {
            Anchor = anchor;
            Elements = elements.Select<string, SelectionPathElement>(e =>
            {
                if (e == "..")
                    return new SelectParent();
                if (e.Contains("?") || e.Contains("*"))
                    return new SelectChildPattern(e);
                return new SelectChildName(e);
            }).ToArray();
        }

        /// <summary>
        ///     Gets the anchor.
        /// </summary>
        /// <value>The anchor.</value>
        public ActorRef Anchor { get; private set; }

        /// <summary>
        ///     Gets or sets the elements.
        /// </summary>
        /// <value>The elements.</value>
        public SelectionPathElement[] Elements { get; set; }

        /// <summary>
        /// <see cref="string"/> representation of all of the elements in the <see cref="ActorSelection"/> path.
        /// </summary>
        public string PathString { get { return string.Join("/", Elements.Select(x => x.ToString())); } }

        /// <summary>
        ///     Posts a message to this ActorSelection.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        public void Tell(object message, ActorRef sender)
        {
            Deliver(message, sender, 0, Anchor);
        }

        /// <summary>
        ///     Posts a message to this ActorSelection.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Tell(object message)
        {
            var sender = ActorRefs.NoSender;
            if (ActorCell.Current != null && ActorCell.Current.Self != null)
                sender = ActorCell.Current.Self;

            Deliver(message, sender, 0, Anchor);
        }

        public Task<ActorRef> ResolveOne(TimeSpan timeout)
        {
            return InnerResolveOne(timeout);
        }

        private async Task<ActorRef> InnerResolveOne(TimeSpan timeout)
        {
            try
            {
                var identity = await this.Ask<ActorIdentity>(new Identify(null), timeout);
                return identity.Subject;
            }
            catch
            {
                throw new ActorNotFoundException();
            }
        }

        /// <summary>
        ///     Delivers the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <param name="pathIndex">Index of the path.</param>
        /// <param name="current">The current.</param>
        private void Deliver(object message, ActorRef sender, int pathIndex, ActorRef current)
        {
            if (pathIndex == Elements.Length)
            {
                current.Tell(message, sender);
            }
            else
            {
                var element = Elements[pathIndex];
                if (current is ActorRefWithCell)
                {
                    var withCell = (ActorRefWithCell) current;
                    if (element is SelectParent)
                        Deliver(message, sender, pathIndex + 1, withCell.Parent);
                    else if (element is SelectChildName)
                        Deliver(message, sender, pathIndex + 1,
                            withCell.GetSingleChild(element.AsInstanceOf<SelectChildName>().Name));
                    else if (element is SelectChildPattern)
                    {
                        var pattern = element as SelectChildPattern;
                        var children =
                            withCell.Children.Where(c => c.Path.Name.Like(pattern.PatternStr));
                        foreach (ActorRef matchingChild in children)
                        {
                            Deliver(message, sender, pathIndex + 1, matchingChild);
                        }
                    }
                }
                else
                {
                    var rest = Elements.Skip(pathIndex).ToArray();
                    current.Tell(new ActorSelectionMessage(message, rest), sender);
                }
            }
        }

        /// <summary>
        ///     INTERNAL API
        ///     Convenience method used by remoting when receiving <see cref="ActorSelectionMessage" /> from a remote
        ///     actor.
        /// </summary>
        internal static void DeliverSelection(InternalActorRef anchor, ActorRef sender, ActorSelectionMessage sel)
        {
            var actorSelection = new ActorSelection(anchor, sel.Elements);
            actorSelection.Tell(sel.Message, sender);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ActorSelection) obj);
        }

        protected bool Equals(ActorSelection other)
        {
            return Equals(Anchor, other.Anchor) && Equals(Elements, other.Elements);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Anchor != null ? Anchor.GetHashCode() : 0 * 397;
                return Elements.Aggregate(hashCode, (current, element) => current ^ element.GetHashCode() * 17);
            }
        }
    }

    /// <summary>
    ///     Class ActorSelectionMessage.
    /// </summary>
    public class ActorSelectionMessage : AutoReceivedMessage, PossiblyHarmful
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelectionMessage" /> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="elements">The elements.</param>
        public ActorSelectionMessage(object message, SelectionPathElement[] elements, bool wildCardFanOut = false)
        {
            Message = message;
            Elements = elements;
            WildCardFanOut = wildCardFanOut;
        }

        /// <summary>
        ///     The message that should be delivered to this ActorSelection.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; private set; }

        /// <summary>
        ///     The elements, e.g. "foo/bar/baz".
        /// </summary>
        /// <value>The elements.</value>
        public SelectionPathElement[] Elements { get; private set; }

        public bool WildCardFanOut { get; private set; }

        public override string ToString()
        {
            return string.Format("ActorSelectionMessage - Message: {0} - WildCartFanOut: {1} - Elements: {2}",
                Message, WildCardFanOut, string.Join<SelectionPathElement>("/", Elements));
        }
    }

    /// <summary>
    ///     Class SelectionPathElement.
    /// </summary>
    public abstract class SelectionPathElement
    {
    }

    /// <summary>
    ///     Class SelectChildName.
    /// </summary>
    public class SelectChildName : SelectionPathElement
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SelectChildName" /> class.
        /// </summary>
        /// <param name="name">The name.</param>
        public SelectChildName(string name)
        {
            Name = name;
        }

        /// <summary>
        ///     Gets or sets the actor name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; }

        /// <summary>
        ///     Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    ///     Class SelectChildPattern.
    /// </summary>
    public class SelectChildPattern : SelectionPathElement
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SelectChildPattern" /> class.
        /// </summary>
        /// <param name="patternStr">The pattern string.</param>
        public SelectChildPattern(string patternStr)
        {
            PatternStr = patternStr;
        }

        /// <summary>
        ///     Gets the pattern string.
        /// </summary>
        /// <value>The pattern string.</value>
        public string PatternStr { get; private set; }

        /// <summary>
        ///     Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return PatternStr.ToString(CultureInfo.InvariantCulture);
        }
    }


    /// <summary>
    ///     Class SelectParent.
    /// </summary>
    public class SelectParent : SelectionPathElement
    {
        /// <summary>
        ///     Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return "..";
        }
    }
}
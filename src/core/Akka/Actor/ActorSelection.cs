//-----------------------------------------------------------------------
// <copyright file="ActorSelection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Akka.Util;
using Akka.Util.Internal;
using Akka.Util.Internal.Collections;

namespace Akka.Actor
{      
    /// <summary>
    ///     Class ActorSelection.
    /// </summary>
    public class ActorSelection : ICanTell
    {
        /// <summary>
        ///     Gets the anchor.
        /// </summary>
        /// <value>The anchor.</value>
        public IActorRef Anchor { get; private set; }

        /// <summary>
        ///     Gets or sets the elements.
        /// </summary>
        /// <value>The elements.</value>
        public SelectionPathElement[] Path { get; set; }

        /// <summary>
        /// <see cref="string"/> representation of all of the elements in the <see cref="ActorSelection"/> path.
        /// </summary>
        public string PathString { get { return string.Join("/", Path.Select(x => x.ToString())); } }

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
        public ActorSelection(IActorRef anchor, SelectionPathElement[] path)
        {
            Anchor = anchor;
            Path = path;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="path">The path.</param>
        public ActorSelection(IActorRef anchor, string path)
            : this(anchor, path == "" ? new string[] {} : path.Split('/'))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="elements">The elements.</param>
        public ActorSelection(IActorRef anchor, IEnumerable<string> elements)
        {
            Anchor = anchor;
            Path = elements
                .Select<string, SelectionPathElement>(e =>
                {
                    if (e.Contains("?") || e.Contains("*"))
                        return new SelectChildPattern(e);
                    if (e == "..")
                        return new SelectParent();
                    return new SelectChildName(e);
                })
                .ToArray();
        }

        /// <summary>
        ///     Posts a message to this ActorSelection.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        public void Tell(object message, IActorRef sender = null)
        {
            if (sender == null && ActorCell.Current != null && ActorCell.Current.Self != null)
                sender = ActorCell.Current.Self;

            DeliverSelection(Anchor as IInternalActorRef, sender, 
                new ActorSelectionMessage(message, Path, wildCardFanOut: false));
        }

        public Task<IActorRef> ResolveOne(TimeSpan timeout)
        {
            return InnerResolveOne(timeout);
        }

        private async Task<IActorRef> InnerResolveOne(TimeSpan timeout)
        {
            try
            {
                var identity = await this.Ask<ActorIdentity>(new Identify(null), timeout);
                if(identity.Subject == null)
                    throw new ActorNotFoundException("subject was null");

                return identity.Subject;
            }
            catch(Exception ex)
            {
                throw new ActorNotFoundException("Exception ocurred while resolving ActorSelection", ex);
            }
        }
        
        /// <summary>
        ///     INTERNAL API
        ///     Convenience method used by remoting when receiving <see cref="ActorSelectionMessage" /> from a remote
        ///     actor.
        /// </summary>
        internal static void DeliverSelection(IInternalActorRef anchor, IActorRef sender, ActorSelectionMessage sel)
        {
            if (sel.Elements.IsNullOrEmpty())
            {
                anchor.Tell(sel.Message, sender);
            }
            else
            {
                var iter = sel.Elements.Iterator();

                Action<IInternalActorRef> rec = null;
                rec = @ref => @ref.Match()
                    .With<ActorRefWithCell>(refWithCell =>
                    {
                        var emptyRef = new EmptyLocalActorRef(refWithCell.Provider, anchor.Path/sel.Elements.Select(el => el.ToString()), refWithCell.Underlying.System.EventStream);

                        iter.Next()
                            .Match()
                            .With<SelectParent>(_ =>
                            {
                                var parent = @ref.Parent;
                                if (iter.IsEmpty())
                                    parent.Tell(sel.Message, sender);
                                else
                                    rec(parent);
                            })
                            .With<SelectChildName>(name =>
                            {
                                var child = refWithCell.GetSingleChild(name.Name);
                                if (child is Nobody)
                                {
                                    if (!sel.WildCardFanOut) 
                                        emptyRef.Tell(sel, sender);
                                }
                                else if (iter.IsEmpty())
                                {
                                    child.Tell(sel.Message, sender);
                                }
                                else
                                {
                                    rec(child);
                                }

                            })
                            .With<SelectChildPattern>(p =>
                            {
                                var children = refWithCell.Children;
                                var matchingChildren = children
                                    .Where(c => c.Path.Name.Like(p.PatternStr))
                                    .ToList();

                                if (iter.IsEmpty())
                                {
                                    if(matchingChildren.Count ==0 && !sel.WildCardFanOut)
                                        emptyRef.Tell(sel, sender);
                                    else
                                        matchingChildren.ForEach(child => child.Tell(sel.Message, sender));
                                }
                                else
                                {
                                    if (matchingChildren.Count == 0 && !sel.WildCardFanOut)
                                        emptyRef.Tell(sel, sender);
                                    else
                                    {
                                        var m = new ActorSelectionMessage(sel.Message, iter.ToVector().ToArray(), 
                                            sel.WildCardFanOut || matchingChildren.Count > 1);
                                        matchingChildren.ForEach(child => DeliverSelection(child as IInternalActorRef, sender, m));
                                    }
                                }
                            });
                    })
                    .Default(_ => @ref.Tell(new ActorSelectionMessage(sel.Message, iter.ToVector().ToArray()), sender));

                rec(anchor);
            }
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ActorSelection) obj);
        }

        protected bool Equals(ActorSelection other)
        {
            return Equals(Anchor, other.Anchor) && Equals(Path, other.Path);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Anchor != null ? Anchor.GetHashCode() : 0 * 397;
                return Path.Aggregate(hashCode, (current, element) => current ^ element.GetHashCode() * 17);
            }
        }
    }

    /// <summary>
    ///     Class ActorSelectionMessage.
    /// </summary>
    public class ActorSelectionMessage : IAutoReceivedMessage, IPossiblyHarmful
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelectionMessage" /> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="elements">The elements.</param>
        /// <param name="wildCardFanOut"></param>
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


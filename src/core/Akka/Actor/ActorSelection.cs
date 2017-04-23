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
using System.Text;
using System.Threading.Tasks;
using Akka.Util;
using Akka.Util.Internal;
using Akka.Util.Internal.Collections;

namespace Akka.Actor
{
    /// <summary>
    /// This class represents a logical view of a section of an <see cref="ActorSystem">ActorSystem's</see>
    /// tree of actors that allows for broadcasting of messages to that section.
    /// </summary>
    public class ActorSelection : ICanTell
    {
        /// <summary>
        /// Gets the anchor.
        /// </summary>
        public IActorRef Anchor { get; private set; }

        /// <summary>
        /// Gets the elements.
        /// </summary>
        public SelectionPathElement[] Path { get; private set; }

        /// <summary>
        /// A string representation of all of the elements in the <see cref="ActorSelection"/> path,
        /// starting with "/" and separated with "/".
        /// </summary>
        public string PathString => "/" + string.Join("/", Path.Select(x => x.ToString()));

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        public ActorSelection()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="path">The path.</param>
        public ActorSelection(IActorRef anchor, SelectionPathElement[] path)
        {
            Anchor = anchor;
            Path = path;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="path">The path.</param>
        public ActorSelection(IActorRef anchor, string path)
            : this(anchor, path == "" ? new string[] {} : path.Split('/'))
        {
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSelection" /> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="elements">The elements.</param>
        public ActorSelection(IActorRef anchor, IEnumerable<string> elements)
        {
            Anchor = anchor;
            
            Path = elements
                .Where(s=>!string.IsNullOrWhiteSpace(s))
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
        /// Sends a message to this ActorSelection.
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="sender">The actor that sent the message</param>
        public void Tell(object message, IActorRef sender = null)
        {
            if (sender == null && ActorCell.Current != null && ActorCell.Current.Self != null)
                sender = ActorCell.Current.Self;

            DeliverSelection(Anchor as IInternalActorRef, sender, 
                new ActorSelectionMessage(message, Path, wildCardFanOut: false));
        }

        /// <summary>
        /// Resolves the <see cref="IActorRef"/> matching this selection.
        /// The result is returned as a Task that is completed with the <see cref="IActorRef"/>
        /// if such an actor exists. It is completed with failure <see cref="ActorNotFoundException"/> if
        /// no such actor exists or the identification didn't complete within the supplied <paramref name="timeout"/>.
        /// 
        /// Under the hood it talks to the actor to verify its existence and acquire its <see cref="IActorRef"/>
        /// </summary>
        /// <param name="timeout">
        /// The amount of time to wait while resolving the selection before terminating the operation and generating an error.
        /// </param>
        /// <exception cref="ActorNotFoundException">
        /// This exception is thrown if no such actor exists or the identification didn't complete within the supplied <paramref name="timeout"/>.
        /// </exception>
        /// <returns>TBD</returns>
        public Task<IActorRef> ResolveOne(TimeSpan timeout) => InnerResolveOne(timeout);

        private async Task<IActorRef> InnerResolveOne(TimeSpan timeout)
        {
            try
            {
                var identity = await this.Ask<ActorIdentity>(new Identify(null), timeout).ConfigureAwait(false);
                if(identity.Subject == null)
                    throw new ActorNotFoundException("subject was null");

                return identity.Subject;
            }
            catch(Exception ex)
            {
                throw new ActorNotFoundException("Exception occurred while resolving ActorSelection", ex);
            }
        }
        
        /// <summary>
        /// INTERNAL API
        /// Convenience method used by remoting when receiving <see cref="ActorSelectionMessage" /> from a remote
        /// actor.
        /// </summary>
        /// <param name="anchor">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="sel">TBD</param>
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
                                    // don't send to emptyRef after wildcard fan-out
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
                                // fan-out when there is a wildcard
                                var children = refWithCell.Children;
                                var matchingChildren = children
                                    .Where(c => c.Path.Name.Like(p.PatternStr))
                                    .ToList();

                                if (iter.IsEmpty())
                                {
                                    if(matchingChildren.Count == 0 && !sel.WildCardFanOut)
                                        emptyRef.Tell(sel, sender);
                                    else
                                        matchingChildren.ForEach(child => child.Tell(sel.Message, sender));
                                }
                                else
                                {
                                    // don't send to emptyRef after wildcard fan-out 
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
                    // foreign ref, continue by sending ActorSelectionMessage to it with remaining elements
                    .Default(_ => @ref.Tell(new ActorSelectionMessage(sel.Message, iter.ToVector().ToArray()), sender));

                rec(anchor);
            }
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ActorSelection)obj);
        }

        /// <summary>
        /// Determines whether the specified actor selection, is equal to this instance.
        /// </summary>
        /// <param name="other">The actor selection to compare.</param>
        /// <returns><c>true</c> if the specified router is equal to this instance; otherwise, <c>false</c>.</returns>
        protected bool Equals(ActorSelection other)
        {
            return Equals(Anchor, other.Anchor) && Equals(PathString, other.PathString);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Anchor?.GetHashCode() ?? 0) * 397) ^ (PathString?.GetHashCode() ?? 0);
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append($"ActorSelection[Anchor({Anchor.Path}");
            if (Anchor.Path.Uid != ActorCell.UndefinedUid)
                builder.Append($"#{Anchor.Path.Uid}");
            builder.Append($"), Path({PathString})]");
            return builder.ToString();
        }
    }

    /// <summary>
    /// Class ActorSelectionMessage.
    /// </summary>
    public class ActorSelectionMessage : IAutoReceivedMessage, IPossiblyHarmful
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSelectionMessage" /> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="elements">The elements.</param>
        /// <param name="wildCardFanOut">TBD</param>
        public ActorSelectionMessage(object message, SelectionPathElement[] elements, bool wildCardFanOut = false)
        {
            Message = message;
            Elements = elements;
            WildCardFanOut = wildCardFanOut;
        }

        /// <summary>
        /// The message that should be delivered to this ActorSelection.
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// The elements, e.g. "foo/bar/baz".
        /// </summary>
        public SelectionPathElement[] Elements { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool WildCardFanOut { get; }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            var elements = string.Join<SelectionPathElement>("/", Elements);
            return $"ActorSelectionMessage - Message: {Message} - WildCartFanOut: {WildCardFanOut} - Elements: {elements}";
        }
    }

    /// <summary>
    /// Class SelectionPathElement.
    /// </summary>
    public abstract class SelectionPathElement
    {
    }

    /// <summary>
    /// Class SelectChildName.
    /// </summary>
    public class SelectChildName : SelectionPathElement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectChildName" /> class.
        /// </summary>
        /// <param name="name">The name.</param>
        public SelectChildName(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Gets the actor name.
        /// </summary>
        public string Name { get; }

        protected bool Equals(SelectChildName other)
        {
            return string.Equals(Name, other.Name);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((SelectChildName)obj);
        }

        public override int GetHashCode()
        {
            return (Name != null ? Name.GetHashCode() : 0);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Class SelectChildPattern.
    /// </summary>
    public class SelectChildPattern : SelectionPathElement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SelectChildPattern" /> class.
        /// </summary>
        /// <param name="patternStr">The pattern string.</param>
        public SelectChildPattern(string patternStr)
        {
            PatternStr = patternStr;
        }

        /// <summary>
        /// Gets the pattern string.
        /// </summary>
        public string PatternStr { get; }

        protected bool Equals(SelectChildPattern other)
        {
            return string.Equals(PatternStr, other.PatternStr);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((SelectChildPattern)obj);
        }

        public override int GetHashCode()
        {
            return (PatternStr != null ? PatternStr.GetHashCode() : 0);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString() => PatternStr;
    }


    /// <summary>
    /// Class SelectParent.
    /// </summary>
    public class SelectParent : SelectionPathElement
    {
        public override bool Equals(object obj) => !ReferenceEquals(obj, null) && obj is SelectParent;
        public override int GetHashCode() => nameof(SelectParent).GetHashCode();

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString() => "..";
    }
}

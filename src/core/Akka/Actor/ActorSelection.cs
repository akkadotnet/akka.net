//-----------------------------------------------------------------------
// <copyright file="ActorSelection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
                .Where(s => !string.IsNullOrWhiteSpace(s))
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
        /// <returns>A Task that will be completed with the <see cref="IActorRef"/>, if the actor was found. Otherwise it will be failed with an <see cref="ActorNotFoundException"/>.</returns>
        public Task<IActorRef> ResolveOne(TimeSpan timeout) => InnerResolveOne(timeout, CancellationToken.None);

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
        /// <param name="ct">
        /// The cancellation token that can be used to cancel the operation.
        /// </param>
        /// <exception cref="ActorNotFoundException">
        /// This exception is thrown if no such actor exists or the identification didn't complete within the supplied <paramref name="timeout"/>.
        /// </exception>
        /// <returns>A Task that will be completed with the <see cref="IActorRef"/>, if the actor was found. Otherwise it will be failed with an <see cref="ActorNotFoundException"/>.</returns>
        public Task<IActorRef> ResolveOne(TimeSpan timeout, CancellationToken ct) => InnerResolveOne(timeout, ct);

        private async Task<IActorRef> InnerResolveOne(TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                var identity = await this.Ask<ActorIdentity>(new Identify(null), timeout, ct).ConfigureAwait(false);
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

                void Rec(IInternalActorRef actorRef)
                {
                    if (actorRef is ActorRefWithCell refWithCell)
                    {
                        var emptyRef = new EmptyLocalActorRef(
                            provider: refWithCell.Provider,
                            path: anchor.Path / sel.Elements.Select(el => el.ToString()),
                            eventStream: refWithCell.Underlying.System.EventStream);

                        switch(iter.Next())
                        {
                            case SelectParent _:
                                var parent = actorRef.Parent;

                                if (iter.IsEmpty())
                                    parent.Tell(sel.Message, sender);
                                else
                                    Rec(parent);

                                break;
                            case SelectChildName name:
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
                                    Rec(child);
                                }

                                break;
                            case SelectChildPattern pattern:
                                // fan-out when there is a wildcard
                                var matchingChildren = refWithCell.Children
                                    .Where(c => c.Path.Name.Like(pattern.PatternStr))
                                    .ToList();

                                if (iter.IsEmpty())
                                {
                                    if (matchingChildren.Count == 0 && !sel.WildCardFanOut)
                                        emptyRef.Tell(sel, sender);
                                    else
                                    {
                                        for (var i = 0; i < matchingChildren.Count; i++)
                                            matchingChildren[i].Tell(sel.Message, sender);
                                    }
                                }
                                else
                                {
                                    // don't send to emptyRef after wildcard fan-out 
                                    if (matchingChildren.Count == 0 && !sel.WildCardFanOut)
                                        emptyRef.Tell(sel, sender);
                                    else
                                    {
                                        var message = new ActorSelectionMessage(
                                            message: sel.Message,
                                            elements: iter.ToVector().ToArray(),
                                            wildCardFanOut: sel.WildCardFanOut || matchingChildren.Count > 1);

                                        for(var i = 0; i < matchingChildren.Count; i++)
                                            DeliverSelection(matchingChildren[i] as IInternalActorRef, sender, message);
                                    }
                                }
                                break;
                        }
                    }
                    else
                    {
                        // foreign ref, continue by sending ActorSelectionMessage to it with remaining elements
                        actorRef.Tell(new ActorSelectionMessage(sel.Message, iter.ToVector().ToArray()), sender);
                    }
                }

                Rec(anchor);
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ActorSelection)obj);
        }

        /// <inheritdoc/>
        protected bool Equals(ActorSelection other)
        {
            return Equals(Anchor, other.Anchor) && Equals(PathString, other.PathString);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Anchor?.GetHashCode() ?? 0) * 397) ^ (PathString?.GetHashCode() ?? 0);
            }
        }

        /// <inheritdoc/>
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
    /// Used to deliver messages via <see cref="ActorSelection"/>.
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
        /// When <c>true</c>, indicates that this <see cref="ActorSelection"/> includes wildcards.
        /// </summary>
        public bool WildCardFanOut { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            var elements = string.Join<SelectionPathElement>("/", Elements);
            return $"ActorSelectionMessage - Message: {Message} - WildCartFanOut: {WildCardFanOut} - Elements: {elements}";
        }

        /// <summary>
        /// Creates a deep copy of the <see cref="ActorSelectionMessage"/> with the provided properties.
        /// </summary>
        /// <param name="message">Optional. The new message to deliver.</param>
        /// <param name="elements">Optional. The new elements on the actor selection.</param>
        /// <param name="wildCardFanOut">Optional. Indicates whether or not we're delivering a wildcard <see cref="ActorSelection"/>.</param>
        /// <returns>A new <see cref="ActorSelectionMessage"/>.</returns>
        public ActorSelectionMessage Copy(object message = null, SelectionPathElement[] elements = null,
            bool? wildCardFanOut = null)
        {
            return new ActorSelectionMessage(message ?? Message, elements ?? Elements, wildCardFanOut ?? WildCardFanOut);
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

        /// <inheritdoc/>
        protected bool Equals(SelectChildName other)
        {
            return string.Equals(Name, other.Name);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((SelectChildName)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => Name?.GetHashCode() ?? 0;

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        protected bool Equals(SelectChildPattern other) => string.Equals(PatternStr, other.PatternStr);

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((SelectChildPattern)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => PatternStr?.GetHashCode() ?? 0;

        /// <inheritdoc/>
        public override string ToString() => PatternStr;
    }


    /// <summary>
    /// Class SelectParent.
    /// </summary>
    public class SelectParent : SelectionPathElement
    {
        /// <inheritdoc/>
        public override bool Equals(object obj) => !ReferenceEquals(obj, null) && obj is SelectParent;

        /// <inheritdoc/>
        public override int GetHashCode() => nameof(SelectParent).GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => "..";
    }
}

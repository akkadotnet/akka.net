﻿using System.Collections.Generic;
using System.Linq;

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
        ///     Tells the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        public void Tell(object message, ActorRef sender)
        {
            Deliver(message, sender, 0, Anchor);
        }

        /// <summary>
        ///     Tells the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Tell(object message)
        {
            ActorRef sender = ActorRef.NoSender;
            if (ActorCell.Current != null && ActorCell.Current.Self != null)
                sender = ActorCell.Current.Self;

            Deliver(message, sender, 0, Anchor);
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
                SelectionPathElement element = Elements[pathIndex];
                if (current is ActorRefWithCell)
                {
                    var withCell = (ActorRefWithCell) current;
                    if (element is SelectParent)
                        Deliver(message, sender, pathIndex + 1, withCell.Parent);
                    else if (element is SelectChildName)
                        Deliver(message, sender, pathIndex + 1,
                            withCell.GetSingleChild(element.AsInstanceOf<SelectChildName>().Name));
                }
                else
                {
                    SelectionPathElement[] rest = Elements.Skip(pathIndex).ToArray();
                    current.Tell(new ActorSelectionMessage(message, rest), sender);
                }
            }
        }
    }

    /// <summary>
    ///     Class ActorSelectionMessage.
    /// </summary>
    public class ActorSelectionMessage : AutoReceivedMessage
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorSelectionMessage" /> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="elements">The elements.</param>
        public ActorSelectionMessage(object message, SelectionPathElement[] elements)
        {
            Message = message;
            Elements = elements;
        }

        /// <summary>
        ///     Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; private set; }

        /// <summary>
        ///     Gets or sets the elements.
        /// </summary>
        /// <value>The elements.</value>
        public SelectionPathElement[] Elements { get; private set; }
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
        ///     Gets or sets the name.
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
    ///     Class Pattern.
    /// </summary>
    public class Pattern
    {
    }

    /// <summary>
    ///     Class Helpers.
    /// </summary>
    public static class Helpers
    {
        /// <summary>
        ///     Makes the pattern.
        /// </summary>
        /// <param name="patternStr">The pattern string.</param>
        /// <returns>Pattern.</returns>
        public static Pattern MakePattern(string patternStr)
        {
            return new Pattern();
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
            Pattern = Helpers.MakePattern(patternStr);
        }

        /// <summary>
        ///     Gets or sets the pattern.
        /// </summary>
        /// <value>The pattern.</value>
        public Pattern Pattern { get; private set; }

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
            return Pattern.ToString();
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
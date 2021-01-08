//-----------------------------------------------------------------------
// <copyright file="EventSequences.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IEventSequence
    {
        /// <summary>
        /// TBD
        /// </summary>
        IEnumerable<object> Events { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IEmptyEventSequence : IEventSequence { }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class EmptyEventSequence : IEmptyEventSequence, IEquatable<IEventSequence>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly EmptyEventSequence Instance = new EmptyEventSequence();

        private EmptyEventSequence() { }

        /// <summary>
        /// TBD
        /// </summary>
        public IEnumerable<object> Events => Enumerable.Empty<object>();

        /// <inheritdoc/>
        public bool Equals(IEventSequence other)
        {
            return other is EmptyEventSequence;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as IEventSequence);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [Serializable]
    public class EventSequence<T> : IEventSequence, IEquatable<IEventSequence>
    {
        private readonly IList<object> _events;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="events">TBD</param>
        public EventSequence(IEnumerable<object> events)
        {
            _events = events.ToList();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IEnumerable<object> Events => _events;

        /// <inheritdoc/>
        public bool Equals(IEventSequence other)
        {
            return other != null && _events.SequenceEqual(other.Events);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as IEventSequence);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public struct SingleEventSequence : IEventSequence, IEquatable<IEventSequence>
    {
        private readonly object[] _events;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        public SingleEventSequence(object e) : this()
        {
            _events = new[] { e };
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IEnumerable<object> Events => _events;

        /// <inheritdoc/>
        public bool Equals(IEventSequence other)
        {
            if (other == null) return false;
            var e = other.Events.FirstOrDefault();
            return e != null && e.Equals(_events[0]) && other.Events.Count() == 1;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as IEventSequence);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class EventSequence
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static IEventSequence Empty = EmptyEventSequence.Instance;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        /// <returns>TBD</returns>
        public static IEventSequence Single(object e)
        {
            return new SingleEventSequence(e);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="events">TBD</param>
        /// <returns>TBD</returns>
        public static IEventSequence Create(params object[] events)
        {
            return new EventSequence<object>(events);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="events">TBD</param>
        /// <returns>TBD</returns>
        public static IEventSequence Create(IEnumerable<object> events)
        {
            return new EventSequence<object>(events);
        }
    }
}

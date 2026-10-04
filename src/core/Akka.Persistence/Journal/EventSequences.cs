//-----------------------------------------------------------------------
// <copyright file="EventSequences.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// Represents zero or more events produced by an event adapter.
    /// </summary>
    public interface IEventSequence
    {
        /// <summary>
        /// Events in this sequence, in order.
        /// </summary>
        IEnumerable<object> Events { get; }
    }

    /// <summary>
    /// Marker interface for an event sequence containing no events.
    /// </summary>
    public interface IEmptyEventSequence : IEventSequence { }

    /// <summary>
    /// An event sequence containing no events.
    /// </summary>
    [Serializable]
    public sealed class EmptyEventSequence : IEmptyEventSequence, IEquatable<IEventSequence>
    {
        /// <summary>
        /// The singleton empty event sequence.
        /// </summary>
        public static readonly EmptyEventSequence Instance = new();

        private EmptyEventSequence() { }

        /// <summary>
        /// An empty sequence of events.
        /// </summary>
        public IEnumerable<object> Events => Enumerable.Empty<object>();

       
        public bool Equals(IEventSequence other)
        {
            return other is EmptyEventSequence;
        }

       
        public override bool Equals(object obj)
        {
            return Equals(obj as IEventSequence);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
        /// </returns>
        public override int GetHashCode()
        {
            return typeof(EmptyEventSequence).GetHashCode();
        }
    }

    /// <summary>
    /// An event sequence containing zero or more supplied events.
    /// </summary>
    /// <typeparam name="T">Generic type argument for this sequence type; it does not constrain the event objects exposed through <see cref="Events"/>.</typeparam>
    [Serializable]
    public class EventSequence<T> : IEventSequence, IEquatable<IEventSequence>
    {
        private readonly IList<object> _events;
        /// <summary>
        /// Creates an event sequence from the supplied events.
        /// </summary>
        /// <param name="events">Events to include in the sequence, in order.</param>
        public EventSequence(IEnumerable<object> events)
        {
            _events = events.ToList();
        }

        /// <summary>
        /// Events in this sequence, in order.
        /// </summary>
        public IEnumerable<object> Events => _events;

        
        public bool Equals(IEventSequence other)
        {
            return other != null && _events.SequenceEqual(other.Events);
        }

        
        public override bool Equals(object obj)
        {
            return Equals(obj as IEventSequence);
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
                int hash = 17;
                foreach (var item in _events)
                {
                    hash = hash * 23 + (item?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }
    }

    /// <summary>
    /// An event sequence containing exactly one event.
    /// </summary>
    [Serializable]
    public struct SingleEventSequence : IEventSequence, IEquatable<IEventSequence>
    {
        private readonly object[] _events;
        /// <summary>
        /// Creates an event sequence containing one event.
        /// </summary>
        /// <param name="e">Event to include in the sequence.</param>
        public SingleEventSequence(object e) : this()
        {
            _events = new[] { e };
        }

        /// <summary>
        /// The single event in this sequence.
        /// </summary>
        public IEnumerable<object> Events => _events;

        
        public bool Equals(IEventSequence other)
        {
            if (other == null) return false;
            var e = other.Events.FirstOrDefault();
            return e != null && e.Equals(_events[0]) && other.Events.Count() == 1;
        }

        
        public override bool Equals(object obj)
        {
            return Equals(obj as IEventSequence);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
        /// </returns>
        public override int GetHashCode()
        {
            return _events[0]?.GetHashCode() ?? 0;
        }
    }

    /// <summary>
    /// Factory methods for creating event sequences.
    /// </summary>
    public static class EventSequence
    {
        /// <summary>
        /// The singleton empty event sequence.
        /// </summary>
        public static IEventSequence Empty = EmptyEventSequence.Instance;

        /// <summary>
        /// Creates a sequence containing one event.
        /// </summary>
        /// <param name="e">Event to include in the sequence.</param>
        /// <returns>An event sequence containing <paramref name="e"/>.</returns>
        public static IEventSequence Single(object e)
        {
            return new SingleEventSequence(e);
        }

        /// <summary>
        /// Creates a sequence from the supplied events.
        /// </summary>
        /// <param name="events">Events to include in the sequence, in order.</param>
        /// <returns>An event sequence containing the supplied events.</returns>
        public static IEventSequence Create(params object[] events)
        {
            return new EventSequence<object>(events);
        }

        /// <summary>
        /// Creates a sequence from the supplied events.
        /// </summary>
        /// <param name="events">Events to include in the sequence, in order.</param>
        /// <returns>An event sequence containing the supplied events.</returns>
        public static IEventSequence Create(IEnumerable<object> events)
        {
            return new EventSequence<object>(events);
        }
    }
}

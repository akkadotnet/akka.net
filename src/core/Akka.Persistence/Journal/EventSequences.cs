//-----------------------------------------------------------------------
// <copyright file="EventSequences.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Persistence.Journal
{
    public interface IEventSequence
    {
        IEnumerable<object> Events { get; }
    }

    public interface IEmptyEventSequence : IEventSequence { }

    [Serializable]
    public sealed class EmptyEventSequence : IEmptyEventSequence, IEquatable<IEventSequence>
    {
        public static readonly EmptyEventSequence Instance = new EmptyEventSequence();

        private EmptyEventSequence() { }

        public IEnumerable<object> Events { get { return Enumerable.Empty<object>(); } }

        public bool Equals(IEventSequence other)
        {
            return other is EmptyEventSequence;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as IEventSequence);
        }
    }

    [Serializable]
    public class EventSequence<T> : IEventSequence, IEquatable<IEventSequence>
    {
        private readonly ISet<object> _events;
        public EventSequence(IEnumerable<object> events)
        {
            _events = new HashSet<object>(events);
        }

        public IEnumerable<object> Events { get { return _events; } }

        public bool Equals(IEventSequence other)
        {
            return other != null && _events.SetEquals(other.Events);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as IEventSequence);
        }
    }

    [Serializable]
    public struct SingleEventSequence : IEventSequence, IEquatable<IEventSequence>
    {
        private readonly object[] _events;
        public SingleEventSequence(object e) : this()
        {
            _events = new[] { e };
        }

        public IEnumerable<object> Events { get { return _events; } }

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
    }

    public static class EventSequence
    {
        public static IEventSequence Empty = EmptyEventSequence.Instance;

        public static IEventSequence Single(object e)
        {
            return new SingleEventSequence(e);
        }

        public static IEventSequence Create(params object[] events)
        {
            return new EventSequence<object>(events);
        }

        public static IEventSequence Create(IEnumerable<object> events)
        {
            return new EventSequence<object>(events);
        }
    }
}
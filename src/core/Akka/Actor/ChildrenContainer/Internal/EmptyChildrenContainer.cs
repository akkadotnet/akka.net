﻿//-----------------------------------------------------------------------
// <copyright file="EmptyChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Util.Internal.Collections;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// This is the empty container, shared among all leaf actors.
    /// </summary>
    public class EmptyChildrenContainer : IChildrenContainer
    {
        private static readonly ImmutableDictionary<string, IChildStats> _emptyStats = ImmutableDictionary<string, IChildStats>.Empty;
        private static readonly IChildrenContainer _instance = new EmptyChildrenContainer();

        protected EmptyChildrenContainer()
        {
            //Intentionally left blank
        }

        public static IChildrenContainer Instance { get { return _instance; } }

        public virtual IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            var newMap = _emptyStats.Add(name, stats);
            return NormalChildrenContainer.Create(newMap);
        }

        public IChildrenContainer Remove(IActorRef child)
        {
            return this;
        }

        public bool TryGetByName(string name, out IChildStats stats)
        {
            stats = null;
            return false;
        }

        public bool TryGetByRef(IActorRef actor, out ChildRestartStats childRestartStats)
        {
            childRestartStats = null;
            return false;
        }

        public bool Contains(IActorRef actor)
        {
            return false;
        }

        public IReadOnlyCollection<IInternalActorRef> Children { get { return ImmutableList<IInternalActorRef>.Empty; } }

        public IReadOnlyCollection<ChildRestartStats> Stats { get { return ImmutableList<ChildRestartStats>.Empty; } }

        public IChildrenContainer ShallDie(IActorRef actor)
        {
            return this;
        }

        public virtual IChildrenContainer Reserve(string name)
        {
            return NormalChildrenContainer.Create(_emptyStats.Add(name, ChildNameReserved.Instance));
        }

        public IChildrenContainer Unreserve(string name)
        {
            return this;
        }

        public override string ToString()
        {
            return "No children";
        }

        public virtual bool IsTerminating { get { return false; } }
        public virtual bool IsNormal { get { return true; } }
    }
}


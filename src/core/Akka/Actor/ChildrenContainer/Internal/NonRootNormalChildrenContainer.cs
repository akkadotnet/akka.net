// //-----------------------------------------------------------------------
// // <copyright file="NonRootNormalChildrenContainer.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// MUTABLE
    /// </summary>
    public class NonRootNormalChildrenContainer : IChildrenContainer
    {
        private readonly IDictionary<string, IChildStats> _actorStats;

        public NonRootNormalChildrenContainer(IDictionary<string, IChildStats> actorStats)
        {
            _actorStats = actorStats;
        }

        public IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            _actorStats[name] = stats;
            return this;
        }

        public IChildrenContainer Remove(IActorRef child)
        {
            _actorStats.Remove(child.Path.Name);
            return this;
        }

        public bool TryGetByName(string name, out IChildStats stats)
        {
            return _actorStats.TryGetValue(name, out stats);
        }

        public bool TryGetByRef(IActorRef actor, out ChildRestartStats childRestartStats)
        {
            if (_actorStats.TryGetValue(actor.Path.Name, out var stats))
            {
                //Since the actor exists, ChildRestartStats is the only valid ChildStats.
                if (stats is ChildRestartStats crStats && actor.Equals(crStats.Child))
                {
                    childRestartStats = crStats;
                    return true;
                }
            }
            childRestartStats = null;
            return false;
        }

        public IReadOnlyCollection<IInternalActorRef> Children
        {
            get
            {
                var children = _actorStats.Values
                    .OfType<ChildRestartStats>()
                    .Select(item => item.Child);

                // The children collection must stay lazy evaluated
                return new LazyReadOnlyCollection<IInternalActorRef>(children);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyCollection<ChildRestartStats> Stats
        {
            get
            {
                var children = _actorStats.Values.OfType<ChildRestartStats>();

                return new LazyReadOnlyCollection<ChildRestartStats>(children);
            }
        }
        public IChildrenContainer ShallDie(IActorRef actor)
        {
            return new TerminatingChildrenContainer(_actorStats.ToImmutableDictionary(), actor, SuspendReason.UserRequest.Instance);
        }

        public IChildrenContainer Reserve(string name)
        {
            if (_actorStats.ContainsKey(name))
                throw new InvalidActorNameException($@"Actor name ""{name}"" is not unique!");
            _actorStats[name] = ChildNameReserved.Instance;
            return this;
        }

        public IChildrenContainer Unreserve(string name)
        {
            if (_actorStats.TryGetValue(name, out var stats) && (stats is ChildNameReserved))
            {
                _actorStats.Remove(name);
            }
            return this;
        }

        public bool IsTerminating => false;
        public bool IsNormal => true;
        public bool Contains(IActorRef actor)
        {
            return TryGetByRef(actor, out _);
        }
    }

    /// <summary>
    /// This is the empty container, shared among all leaf actors.
    /// </summary>
    public class EmptyNonRootChildrenContainer : IChildrenContainer
    {
        private static readonly ImmutableDictionary<string, IChildStats> _emptyStats = ImmutableDictionary<string, IChildStats>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        protected EmptyNonRootChildrenContainer()
        {
            //Intentionally left blank
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static IChildrenContainer Instance { get; } = new EmptyNonRootChildrenContainer();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        public virtual IChildrenContainer Add(string name, ChildRestartStats stats)
        {
            var newMap = _emptyStats.Add(name, stats);
            return NormalChildrenContainer.Create(newMap);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        public IChildrenContainer Remove(IActorRef child)
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="stats">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetByName(string name, out IChildStats stats)
        {
            stats = null;
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="childRestartStats">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetByRef(IActorRef actor, out ChildRestartStats childRestartStats)
        {
            childRestartStats = null;
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool Contains(IActorRef actor)
        {
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyCollection<IInternalActorRef> Children { get { return ImmutableList<IInternalActorRef>.Empty; } }

        /// <summary>
        /// TBD
        /// </summary>
        public IReadOnlyCollection<ChildRestartStats> Stats { get { return ImmutableList<ChildRestartStats>.Empty; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public IChildrenContainer ShallDie(IActorRef actor)
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public virtual IChildrenContainer Reserve(string name)
        {
            var dictionary = new Dictionary<string, IChildStats> { [name] = ChildNameReserved.Instance };
            return new NonRootNormalChildrenContainer(dictionary);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public IChildrenContainer Unreserve(string name)
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "No children";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsTerminating { get { return false; } }
        /// <summary>
        /// TBD
        /// </summary>
        public virtual bool IsNormal { get { return true; } }
    }
}
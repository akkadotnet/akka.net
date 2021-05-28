// //-----------------------------------------------------------------------
// // <copyright file="NonRootNormalChildrenContainer.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Generic;
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
            throw new System.NotImplementedException();
        }

        public IChildrenContainer Reserve(string name)
        {
            throw new System.NotImplementedException();
        }

        public IChildrenContainer Unreserve(string name)
        {
            throw new System.NotImplementedException();
        }

        public bool IsTerminating { get; }
        public bool IsNormal { get; }
        public bool Contains(IActorRef actor)
        {
            throw new System.NotImplementedException();
        }
    }
}
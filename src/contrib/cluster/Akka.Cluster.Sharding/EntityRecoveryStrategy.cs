//-----------------------------------------------------------------------
// <copyright file="EntityRecoveryStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Cluster.Sharding
{
    using EntityId = String;

    internal abstract class EntityRecoveryStrategy
    {
        public static EntityRecoveryStrategy AllStrategy => new AllAtOnceEntityRecoveryStrategy();

        public static EntityRecoveryStrategy ConstantStrategy(ActorSystem actorSystem, TimeSpan frequency, int numberOfEntities)
            => new ConstantRateEntityRecoveryStrategy(actorSystem, frequency, numberOfEntities);

        public abstract IImmutableSet<Task<IImmutableSet<EntityId>>> RecoverEntities(IImmutableSet<EntityId> entities);
    }

    internal class AllAtOnceEntityRecoveryStrategy : EntityRecoveryStrategy
    {
        public override IImmutableSet<Task<IImmutableSet<EntityId>>> RecoverEntities(IImmutableSet<EntityId> entities)
        {
            return entities.Count == 0
                ? ImmutableHashSet<Task<IImmutableSet<EntityId>>>.Empty
                : ImmutableHashSet.Create(Task.FromResult(entities));
        }
    }

    internal class ConstantRateEntityRecoveryStrategy : EntityRecoveryStrategy
    {
        private readonly ActorSystem actorSystem;
        private readonly TimeSpan frequency;
        private readonly int numberOfEntities;

        public ConstantRateEntityRecoveryStrategy(ActorSystem actorSystem, TimeSpan frequency, int numberOfEntities)
        {
            this.actorSystem = actorSystem;
            this.frequency = frequency;
            this.numberOfEntities = numberOfEntities;
        }

        public override IImmutableSet<Task<IImmutableSet<EntityId>>> RecoverEntities(IImmutableSet<EntityId> entities)
        {
            var stamp = frequency;
            var builder = ImmutableHashSet<Task<IImmutableSet<EntityId>>>.Empty.ToBuilder();
            foreach (var bucket in entities.Grouped(numberOfEntities))
            {
                var scheduled = ScheduleEntities(stamp, bucket.ToImmutableHashSet());
                builder.Add(scheduled);
                stamp += frequency;
            }
            return builder.ToImmutable();
        }

        private Task<IImmutableSet<EntityId>> ScheduleEntities(TimeSpan interval, IImmutableSet<EntityId> entityIds)
        {
            return After(interval, actorSystem.Scheduler, () => Task.FromResult(entityIds));
        }

        /// <summary>
        /// Returns a Task that will be completed with the success or failure of the provided value after the specified duration.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="value">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="scheduler">TBD</param>
        private static Task<T> After<T>(TimeSpan timeout, IScheduler scheduler, Func<Task<T>> value)
        {
            var promise = new TaskCompletionSource<T>();

            scheduler.Advanced.ScheduleOnce(timeout, () =>
            {
                value().ContinueWith(t =>
                {
                    if (t.IsFaulted || t.IsCanceled)
                        promise.SetCanceled();
                    else
                        promise.SetResult(t.Result);
                });
            });

            return promise.Task;
        }
    }

    public static class EnumerableExtensions
    {
        /// <summary> 
        /// Partitions elements in fixed size
        /// Credits to http://stackoverflow.com/a/13731854/465132
        /// </summary>
        /// <param name="items">TBD</param>
        /// <param name="size">The number of elements per group</param>
        public static IEnumerable<IEnumerable<T>> Grouped<T>(this IEnumerable<T> items, int size)
        {
            return items.Select((item, inx) => new { item, inx })
                .GroupBy(x => x.inx / size)
                .Select(g => g.Select(x => x.item));
        }
    }
}

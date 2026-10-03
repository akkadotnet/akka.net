//-----------------------------------------------------------------------
// <copyright file="JournalSequenceActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Embedded.Internal;

namespace Akka.Persistence.Embedded.Query.Internal
{
    // Ported from Akka.Persistence.Sql's JournalSequenceActor (Apache-2.0, akkadotnet).

    /// <summary>Asks the tracker for the highest ordering below which no gap is still expected to fill.</summary>
    internal sealed class GetMaxOrderingId
    {
        public static GetMaxOrderingId Instance { get; } = new();

        private GetMaxOrderingId()
        {
        }
    }

    /// <summary>Reply to <see cref="GetMaxOrderingId"/>.</summary>
    internal sealed class MaxOrderingId
    {
        public MaxOrderingId(long max)
        {
            Max = max;
        }

        public long Max { get; }
    }

    internal sealed class QueryOrderingIds
    {
        public static QueryOrderingIds Instance { get; } = new();

        private QueryOrderingIds()
        {
        }
    }

    internal sealed class NewOrderingIds
    {
        public NewOrderingIds(long maxOrdering, IReadOnlyList<long> elements)
        {
            MaxOrdering = maxOrdering;
            Elements = elements;
        }

        public long MaxOrdering { get; }

        public IReadOnlyList<long> Elements { get; }
    }

    internal sealed class ScheduleAssumeMaxOrderingId
    {
        public ScheduleAssumeMaxOrderingId(long maxInDatabase)
        {
            MaxInDatabase = maxInDatabase;
        }

        public long MaxInDatabase { get; }
    }

    internal sealed class AssumeMaxOrderingId
    {
        public AssumeMaxOrderingId(long max)
        {
            Max = max;
        }

        public long Max { get; }
    }

    /// <summary>A half-open range [From, Until) of orderings that did not show up.</summary>
    internal sealed class NumericRangeEntry
    {
        public NumericRangeEntry(long from, long until)
        {
            From = from;
            Until = until;
        }

        public long From { get; }

        public long Until { get; }

        public bool InRange(long number) => From <= number && number <= Until;
    }

    internal sealed class MissingElements
    {
        public static readonly MissingElements Empty = new(ImmutableList<NumericRangeEntry>.Empty);

        private readonly ImmutableList<NumericRangeEntry> _elements;

        private MissingElements(ImmutableList<NumericRangeEntry> elements)
        {
            _elements = elements;
        }

        public bool IsEmpty => _elements.IsEmpty;

        public MissingElements AddRange(long from, long until) => new(_elements.Add(new NumericRangeEntry(from, until)));

        public bool Contains(long id)
        {
            foreach (var range in _elements)
            {
                if (range.InRange(id))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Builds the tracker without expression trees, so it works under Native AOT.</summary>
    internal sealed class JournalSequenceActorProducer : IIndirectActorProducer
    {
        private readonly Func<long, int, System.Threading.Tasks.Task<IReadOnlyList<long>>> _queryOrderings;
        private readonly Func<System.Threading.Tasks.Task<long>> _queryMaxOrdering;
        private readonly JournalSequenceSettings _settings;

        public JournalSequenceActorProducer(
            Func<long, int, System.Threading.Tasks.Task<IReadOnlyList<long>>> queryOrderings,
            Func<System.Threading.Tasks.Task<long>> queryMaxOrdering,
            JournalSequenceSettings settings)
        {
            _queryOrderings = queryOrderings;
            _queryMaxOrdering = queryMaxOrdering;
            _settings = settings;
        }

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
        public Type ActorType => typeof(JournalSequenceActor);

        public ActorBase Produce() => new JournalSequenceActor(_queryOrderings, _queryMaxOrdering, _settings);

        public void Release(ActorBase actor)
        {
        }
    }

    /// <summary>
    /// Tracks the highest ordering that is safe to read up to: every ordering below it either exists or has been
    /// missing for <c>max-tries</c> polls. Only used when <c>journal-sequence-retrieval.enabled = on</c>.
    /// </summary>
    internal sealed class JournalSequenceActor : ActorBase, IWithTimers
    {
        private const string AssumeMaxOrderingIdTimerKey = "AssumeMaxOrderingId";
        private const string QueryOrderingIdsTimerKey = "QueryOrderingIds";

        private readonly Func<long, int, System.Threading.Tasks.Task<IReadOnlyList<long>>> _queryOrderings;
        private readonly Func<System.Threading.Tasks.Task<long>> _queryMaxOrdering;
        private readonly JournalSequenceSettings _settings;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public JournalSequenceActor(
            Func<long, int, System.Threading.Tasks.Task<IReadOnlyList<long>>> queryOrderings,
            Func<System.Threading.Tasks.Task<long>> queryMaxOrdering,
            JournalSequenceSettings settings)
        {
            _queryOrderings = queryOrderings;
            _queryMaxOrdering = queryMaxOrdering;
            _settings = settings;
        }

        public ITimerScheduler Timers { get; set; } = null!;

        protected override bool Receive(object message)
            => ReceiveHandler(message, 0, ImmutableDictionary<int, MissingElements>.Empty, 0, _settings.QueryDelay);

        private bool ReceiveHandler(
            object message,
            long currentMaxOrdering,
            IImmutableDictionary<int, MissingElements> missingByCounter,
            int moduloCounter,
            TimeSpan previousDelay)
        {
            switch (message)
            {
                case ScheduleAssumeMaxOrderingId schedule:
                    Timers.StartSingleTimer(
                        AssumeMaxOrderingIdTimerKey,
                        new AssumeMaxOrderingId(schedule.MaxInDatabase),
                        _settings.QueryDelay * _settings.MaxTries);
                    return true;

                case AssumeMaxOrderingId assume:
                    if (currentMaxOrdering < assume.Max)
                        Become(o => ReceiveHandler(o, assume.Max, missingByCounter, moduloCounter, previousDelay));
                    return true;

                case GetMaxOrderingId:
                    Sender.Tell(new MaxOrderingId(currentMaxOrdering));
                    return true;

                case QueryOrderingIds:
                    {
                        var self = Self;
                        var max = currentMaxOrdering;
                        _queryOrderings(max, _settings.BatchSize)
                            .ContinueWith(
                                t => t.IsFaulted
                                    ? (object)new Status.Failure(t.Exception!.GetBaseException())
                                    : new NewOrderingIds(max, t.Result),
                                System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously)
                            .PipeTo(self, self);
                        return true;
                    }

                case NewOrderingIds ids when ids.MaxOrdering < currentMaxOrdering:
                    Self.Tell(QueryOrderingIds.Instance);
                    return true;

                case NewOrderingIds ids:
                    FindGaps(ids.Elements, currentMaxOrdering, missingByCounter, moduloCounter);
                    return true;

                case Status.Failure failure:
                    {
                        var doubled = previousDelay * 2;
                        var newDelay = doubled < _settings.MaxBackoffQueryDelay ? doubled : _settings.MaxBackoffQueryDelay;
                        if (newDelay == _settings.MaxBackoffQueryDelay)
                            _log.Warning("Failed to query max Ordering ID Because of {0}, retrying in {1}", failure, newDelay);

                        ScheduleQuery(newDelay);
                        Become(o => ReceiveHandler(o, currentMaxOrdering, missingByCounter, moduloCounter, newDelay));
                        return true;
                    }

                default:
                    return false;
            }
        }

        private void FindGaps(
            IReadOnlyList<long> elements,
            long currentMaxOrdering,
            IImmutableDictionary<int, MissingElements> missingByCounter,
            int moduloCounter)
        {
            var givenUp = missingByCounter.TryGetValue(moduloCounter, out var given) ? given : MissingElements.Empty;

            var currentMax = currentMaxOrdering;
            var previous = currentMaxOrdering;
            var missing = MissingElements.Empty;
            foreach (var current in elements)
            {
                // every number between the current max and this element has been given up on?
                var allGivenUp = true;
                for (var i = currentMax + 1; i < current; i++)
                {
                    if (!givenUp.Contains(i))
                    {
                        allGivenUp = false;
                        break;
                    }
                }

                var newMax = allGivenUp ? current : currentMax;
                if (previous + 1 != current && newMax != current)
                    missing = missing.AddRange(previous + 1, current);

                currentMax = newMax;
                previous = current;
            }

            var newMissingByCounter = missingByCounter.SetItem(moduloCounter, missing);
            var noGapsFound = missing.IsEmpty;
            var isFullBatch = elements.Count == _settings.BatchSize;
            if (noGapsFound && isFullBatch)
            {
                Self.Tell(QueryOrderingIds.Instance);
                Become(o => ReceiveHandler(o, currentMax, newMissingByCounter, moduloCounter, _settings.QueryDelay));
            }
            else
            {
                ScheduleQuery(_settings.QueryDelay);
                Become(o => ReceiveHandler(o, currentMax, newMissingByCounter, (moduloCounter + 1) % _settings.MaxTries, _settings.QueryDelay));
            }
        }

        private void ScheduleQuery(TimeSpan delay)
            => Timers.StartSingleTimer(QueryOrderingIdsTimerKey, QueryOrderingIds.Instance, delay);

        protected override void PreStart()
        {
            var self = Self;
            self.Tell(QueryOrderingIds.Instance);

            // Start from the current max, after max-tries polls, so a restart does not crawl event by event.
            _queryMaxOrdering().ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                        _log.Debug("Failed to recover fast, using event-by-event recovery instead. Message: [{0}]", t.Exception?.GetBaseException().Message ?? "nothing");
                    else if (t.IsCompletedSuccessfully)
                        self.Tell(new ScheduleAssumeMaxOrderingId(t.Result));
                },
                System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);

            base.PreStart();
        }
    }
}

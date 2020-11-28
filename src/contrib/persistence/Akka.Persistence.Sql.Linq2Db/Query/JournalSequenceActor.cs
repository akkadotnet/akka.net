using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Query.InternalProtocol;
using Akka.Persistence.Sql.Linq2Db.Utility;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Util.Extensions;
using Akka.Util.Internal;

namespace Akka.Persistence.Sql.Linq2Db.Query
{
    public class JournalSequenceActor : ActorBase, IWithTimers
    {
        private JournalSequenceRetrievalConfig _config;
        private IReadJournalDAO _readJournalDao;
        private TimeSpan queryDelay;
        private int maxTries;
        private ILoggingAdapter _log;
        private ActorMaterializer _mat;
        public ITimerScheduler Timers { get; set; }

        public JournalSequenceActor(IReadJournalDAO readJournalDao,
            JournalSequenceRetrievalConfig config)
        {
            _mat = 
                Materializer.CreateSystemMaterializer((ExtendedActorSystem)Context.System,
                ActorMaterializerSettings.Create(Context.System),
                "linq2db-query");
            _readJournalDao = readJournalDao;
            _config = config;
            queryDelay = config.QueryDelay;
            maxTries = config.MaxTries;
            _log = Context.GetLogger();
        }

        private bool _receive(object message)
        {
            return _receive(message, 0,
                ImmutableDictionary<int, MissingElements>.Empty, 0, queryDelay);
        }

        protected bool _receive(object message, long currentMaxOrdering,
            IImmutableDictionary<int, MissingElements> missingByCounter,
            int moduloCounter, TimeSpan previousDelay)
        {
            if (message is ScheduleAssumeMaxOrderingId s)
            {
                var delay = queryDelay.Multiply(maxTries);
                Timers.StartSingleTimer(AssumeMaxOrderingIdTimerKey.Instance,
                    new AssumeMaxOrderingId(s.MaxInDatabase), delay);
            }
            else if (message is AssumeMaxOrderingId a)
            {
                if (currentMaxOrdering < a.Max)
                {
                    Become((o => _receive(o, maxTries, missingByCounter,
                        moduloCounter, previousDelay)));
                }
            }
            else if (message is GetMaxOrderingId)
            {
                Sender.Tell(new MaxOrderingId(currentMaxOrdering));
            }
            else if (message is QueryOrderingIds)
            {
                _readJournalDao
                    .JournalSequence(currentMaxOrdering, _config.BatchSize)
                    .RunWith(Sink.Seq<long>(), _mat).PipeTo(Self, sender: Self,
                        success: res =>
                            new NewOrderingIds(currentMaxOrdering, res));
            }
            else if (message is NewOrderingIds nids)
            {
                if (nids.MaxOrdering < currentMaxOrdering)
                {
                    Self.Tell(new QueryOrderingIds());
                }
                else
                {
                    _findGaps(nids.Elements, currentMaxOrdering,
                        missingByCounter, moduloCounter);
                }
            }
            else if (message is Status.Failure t)
            {
                var newDelay =
                    _config.MaxBackoffQueryDelay.Min(previousDelay.Multiply(2));
                if (newDelay == _config.MaxBackoffQueryDelay)
                {
                    _log.Warning(
                        "Failed to query max Ordering ID Because of {0}, retrying in {1}",
                        t, newDelay);
                }

                _scheduleQuery(newDelay);
                Context.Become(o => _receive(o, currentMaxOrdering,
                    missingByCounter, moduloCounter, newDelay));
            }
            else
            {
                return false;
            }

            return true;
        }

        private void _findGaps(IImmutableList<long> elements,
            long currentMaxOrdering,
            IImmutableDictionary<int, MissingElements> missingByCounter,
            int moduloCounter)
        {
            var givenUp =
                missingByCounter.ContainsKey(moduloCounter)
                    ? missingByCounter[moduloCounter]
                    : MissingElements.Empty;
            var (nextMax, _, missingElems) = elements.Aggregate(
                (currentMax: currentMaxOrdering,
                    previousElement: currentMaxOrdering,
                    missing: MissingElements.Empty),
                (agg, currentElement) =>
                {
                    long newMax = 0;

                    if (new NumericRangeEntry(agg.Item1 + 1, currentElement)
                        .ToEnumerable().ForAll(p => givenUp.Contains(p)))
                    {
                        newMax = currentElement;
                    }
                    else
                    {
                        newMax = agg.currentMax;
                    }

                    MissingElements newMissing;
                    if (agg.previousElement + 1 == currentElement ||
                        newMax == currentElement)
                    {
                        newMissing = agg.missing;
                    }
                    else
                    {
                        newMissing = agg.missing.AddRange(agg.Item2 + 1,
                            currentElement);
                    }

                    return (newMax, currentElement, newMissing);
                });
            var newMissingByCounter =
                missingByCounter.SetItem(moduloCounter, missingElems);
            var noGapsFound = missingElems.Isempty;
            var isFullBatch = elements.Count == _config.BatchSize;
            if (noGapsFound && isFullBatch)
            {
                Self.Tell(new QueryOrderingIds());
                Context.Become(o => _receive(o, nextMax, newMissingByCounter,
                    moduloCounter, queryDelay));
            }
            else
            {
                _scheduleQuery(queryDelay);
                Context.Become(o => _receive(o, nextMax, newMissingByCounter,
                    (moduloCounter + 1) % _config.MaxTries, queryDelay));
            }

        }

        private void _scheduleQuery(TimeSpan delay)
        {
            Timers.StartSingleTimer(QueryOrderingIdsTimerKey.Instance,
                new QueryOrderingIds(), delay);
        }

        protected override bool Receive(object message)
        {
            return _receive(message);
        }

        protected override void PreStart()
        {
            var self = Self;
            self.Tell(new QueryOrderingIds());
            _readJournalDao.MaxJournalSequence().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _log.Info(
                        "Failed to recover fast, using event-by-event recovery instead",
                        t.Exception);
                }
                else if (t.IsCompleted)
                {
                    self.Tell(new ScheduleAssumeMaxOrderingId(t.Result));
                }
            });
            base.PreStart();
        }
    }
}
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Streams;
using Akka.Streams;
using Akka.Streams.Dsl;
using LanguageExt;
using LinqToDB.Data;

namespace Akka.Persistence.Sql.Linq2Db.Journal.DAO
{
    public abstract class BaseJournalDaoWithReadMessages : IJournalDaoWithReadMessages
    {
        protected readonly AkkaPersistenceDataConnectionFactory _connectionFactory;
        protected BaseJournalDaoWithReadMessages(IAdvancedScheduler ec,
            IMaterializer mat, AkkaPersistenceDataConnectionFactory connectionFactory)
        {
            this.ec = ec;
            this.mat = mat;
            _connectionFactory = connectionFactory;
        }
        protected IAdvancedScheduler ec;
        protected IMaterializer mat;

        public abstract Task<Source<Util.Try<ReplayCompletion>, NotUsed>> Messages(DataConnection db, string persistenceId, long fromSequenceNr, long toSequenceNr,
            long max);
        

        
        public Source<Util.Try<ReplayCompletion>, NotUsed> MessagesWithBatch(string persistenceId, long fromSequenceNr,
            long toSequenceNr, int batchSize, Util.Option<(TimeSpan,IScheduler)> refreshInterval)
        {
            var src = Source
                .UnfoldAsync<(long, FlowControl),
                    Seq<Util.Try<ReplayCompletion>>>(
                    (Math.Max(1, fromSequenceNr),
                        FlowControl.Continue.Instance),
                    async opt =>
                    {
                        async Task<Util.Option<((long, FlowControl), Seq<Util.Try<ReplayCompletion>>)>>
                            RetrieveNextBatch()
                        {
                            Seq<
                                Util.Try<ReplayCompletion>> msg;
                            using (var conn =
                                _connectionFactory.GetConnection())
                            {
                                var waited = await Messages(conn, persistenceId,
                                    opt.Item1,
                                    toSequenceNr, batchSize);
                                msg = await waited
                                    .RunWith(
                                            ExtSeq.Seq<Util.Try<ReplayCompletion>>(), mat);
                            }

                            var hasMoreEvents = msg.Count == batchSize;
                            var lastMsg = msg.LastOrDefault();
                            Util.Option<long> lastSeq = Util.Option<long>.None;
                            if (lastMsg != null && lastMsg.IsSuccess)
                            {
                                lastSeq = lastMsg.Success.Select(r => r.repr.SequenceNr);
                            }
                            else if (lastMsg != null &&  lastMsg.Failure.HasValue)
                            {
                                throw lastMsg.Failure.Value;
                            }

                            var hasLastEvent =
                                lastSeq.HasValue &&
                                lastSeq.Value >= toSequenceNr;
                            FlowControl nextControl = null;
                            if (hasLastEvent || opt.Item1 > toSequenceNr)
                            {
                                nextControl = FlowControl.Stop.Instance;
                            }
                            else if (hasMoreEvents)
                            {
                                nextControl = FlowControl.Continue.Instance;
                            }
                            else if (refreshInterval.HasValue == false)
                            {
                                nextControl = FlowControl.Stop.Instance;
                            }
                            else
                            {
                                nextControl = FlowControl.ContinueDelayed
                                    .Instance;
                            }

                            long nextFrom = 0;
                            if (lastSeq.HasValue)
                            {
                                nextFrom = lastSeq.Value + 1;
                            }
                            else
                            {
                                nextFrom = opt.Item1;
                            }

                            return new Util.Option<((long, FlowControl), Seq<Util.Try<ReplayCompletion>>)>((
                                    (nextFrom, nextControl), msg));
                        }

                        switch (opt.Item2)
                        {
                            case FlowControl.Stop _:
                                return Util.Option<((long, FlowControl), Seq<Util.Try<ReplayCompletion>>)>.None;
                            case FlowControl.Continue _:
                                return await RetrieveNextBatch();
                            case FlowControl.ContinueDelayed _ when refreshInterval.HasValue:
                                return await FutureTimeoutSupport.After(refreshInterval.Value.Item1,refreshInterval.Value.Item2, RetrieveNextBatch);
                            default:
                                throw new Exception($"Got invalid FlowControl from Queue! Type : {opt.Item2.GetType()}");
                        }
                    });

            return src.SelectMany(r => r);
        }
    }
}
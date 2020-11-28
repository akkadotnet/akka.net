using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal.DAO;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using LinqToDB;
using LinqToDB.Data;

namespace Akka.Persistence.Sql.Linq2Db.Query.Dao
{
    public abstract class
        BaseByteReadArrayJournalDAO : BaseJournalDaoWithReadMessages,
            IReadJournalDAO
    {
        private bool includeDeleted;
        private ReadJournalConfig _readJournalConfig;
        private FlowPersistentReprSerializer<JournalRow> _serializer;

        protected BaseByteReadArrayJournalDAO(IAdvancedScheduler ec,
            IMaterializer mat,
            AkkaPersistenceDataConnectionFactory connectionFactory,
            ReadJournalConfig readJournalConfig,
            FlowPersistentReprSerializer<JournalRow> serializer) : base(ec, mat,
            connectionFactory)
        {

            _readJournalConfig = readJournalConfig;
            includeDeleted = readJournalConfig.IncludeDeleted;
            _serializer = serializer;
        }

        protected IQueryable<JournalRow> baseQuery(DataConnection connection)
        {
            return connection.GetTable<JournalRow>()
                .Where(jr =>
                    includeDeleted == false || (jr.deleted == false));
        }

        public Source<string, NotUsed> AllPersistenceIdsSource(long max)
        {
            using (var db = _connectionFactory.GetConnection())
            {
                var maxTake = MaxTake(max);
                return Source.From(baseQuery(db)
                    .Select(r => r.persistenceId).Distinct()
                    .Take(maxTake).ToList());
            }

        }

        private static int MaxTake(long max)
        {
            int maxTake;
            if (max > Int32.MaxValue)
            {
                maxTake = Int32.MaxValue;
            }
            else
            {
                maxTake = (int) max;
            }

            return maxTake;
        }

        public Source<
            Akka.Util.Try<(IPersistentRepresentation, IImmutableSet<string>, long)>,
            NotUsed> EventsByTag(string tag, long offset, long maxOffset,
            long max)
        {
            var separator = _readJournalConfig.PluginConfig.TagSeparator;
            var maxTake = MaxTake(max);
            using (var conn = _connectionFactory.GetConnection())
            {
                return Source.From(Queryable.Where<JournalRow>(conn.GetTable<JournalRow>(), r => r.tags.Contains(tag))
                        .OrderBy(r => r.ordering)
                        .Where(r =>
                            r.ordering > offset && r.ordering <= maxOffset)
                        .Take(maxTake).ToList())
                    .Via(perfectlyMatchTag(tag, separator))
                    .Via(_serializer.DeserializeFlow());

            }
        }

        private Flow<JournalRow, JournalRow, NotUsed> perfectlyMatchTag(
            string tag,
            string separator)
        {

            return Flow.Create<JournalRow>().Where(r =>
                (r.tags ?? "")
                .Split(new[] {separator}, StringSplitOptions.RemoveEmptyEntries)
                .Any(t => t.Contains(tag)));
        }

        public override async Task<Source<Akka.Util.Try<ReplayCompletion>, NotUsed>> Messages(
            DataConnection dc, string persistenceId, long fromSequenceNr,
            long toSequenceNr, long max)
        {
            var toTake = MaxTake(max);
            //using (var conn = _connectionFactory.GetConnection())
            //{
                return Source.From(
                        await baseQuery(dc)
                            .Where(r => r.persistenceId == persistenceId
                                        && r.sequenceNumber >= fromSequenceNr
                                        && r.sequenceNumber <= toSequenceNr)
                            .OrderBy(r => r.sequenceNumber)
                            .Take(toTake).ToListAsync())
                    .Via(_serializer.DeserializeFlow())
                    .Select(
                        t =>
                        {
                            try
                            {
                                var val = t.Get();
                                return new Akka.Util.Try<ReplayCompletion>(
                                    new ReplayCompletion()
                                    {
                                        repr = val.Item1, Ordering = val.Item3
                                    });
                            }
                            catch (Exception e)
                            {
                                return new Akka.Util.Try<ReplayCompletion>(e);
                            }
                        });

            //}

        }

        public Source<long, NotUsed> JournalSequence(long offset, long limit)
        {
            var maxTake = MaxTake(limit);
            using (var conn = _connectionFactory.GetConnection())
            {
                return Source.From(Queryable.Where<JournalRow>(conn.GetTable<JournalRow>(), r => r.ordering > offset).Select(r => r.ordering)
                    .OrderBy(r => r).Take(maxTake).ToList());
            }
        }

        public Task<long> MaxJournalSequence()
        {
            using (var db = _connectionFactory.GetConnection())
            {
                return Task.FromResult(Queryable.Select<JournalRow, long>(db.GetTable<JournalRow>(), r => r.ordering).FirstOrDefault());
            }
        }
    }
}
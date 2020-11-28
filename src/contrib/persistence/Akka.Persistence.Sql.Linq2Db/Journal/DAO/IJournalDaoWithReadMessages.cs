using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Streams.Dsl;
using Akka.Util;
using LinqToDB.Data;

namespace Akka.Persistence.Sql.Linq2Db.Journal.DAO
{
    public interface IJournalDaoWithReadMessages
    {
        Task<Source<Try<ReplayCompletion>, NotUsed>> Messages(DataConnection dc,
            string persistenceId, long fromSequenceNr, long toSequenceNr,
            long max);
        Source<Try<ReplayCompletion>,NotUsed> MessagesWithBatch(
            string persistenceId, long fromSequenceNr, long toSequenceNr,
            int batchSize, Option<(TimeSpan,IScheduler)> refreshInterval);
    }
}